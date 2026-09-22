using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.Models;
using InvoiceAnalyzerBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace InvoiceAnalyzerBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoicesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IOcrService _ocr;
        private readonly IAnalyzerService _analyzer;
        private readonly ILogger<InvoicesController> _logger;
        private readonly IHostEnvironment _env;

        public InvoicesController(
            AppDbContext db,
            IOcrService ocr,
            IAnalyzerService analyzer,
            ILogger<InvoicesController> logger,
            IHostEnvironment env)
        {
            _db = db;
            _ocr = ocr;
            _analyzer = analyzer;
            _logger = logger;
            _env = env;
        }

        // POST api/invoices/upload
        // multipart/form-data: file
        [HttpPost("upload")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            // ensure upload folder exists
            var uploadsDir = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploadsDir);

            var uniqueName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsDir, uniqueName);

            await using (var fs = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(fs, cancellationToken);
            }

            // create invoice record minimal
            var invoice = new Invoice
            {
                FilePath = filePath,
                Status = InvoiceStatus.Uploaded,
                UploadedAt = DateTime.UtcNow
            };

            await _db.Invoices.AddAsync(invoice, cancellationToken);

            // create job
            var job = new InvoiceJob
            {
                Invoice = invoice,
                Status = JobStatus.Pending,
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _db.InvoiceJobs.AddAsync(job, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var result = new
            {
                invoiceId = invoice.Id,
                jobId = job.Id
            };

            return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, result);
        }

        // GET api/invoices/jobs/{jobId}
        [HttpGet("jobs/{jobId:guid}")]
        public async Task<IActionResult> GetJobStatus(Guid jobId, CancellationToken cancellationToken)
        {
            var job = await _db.InvoiceJobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => new
                {
                    j.Id,
                    j.InvoiceId,
                    j.Status,
                    j.Progress,
                    j.ResultLog,
                    j.CreatedAt,
                    j.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (job == null) return NotFound();

            return Ok(job);
        }

        // GET api/invoices
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? vendor, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        {
            var q = _db.Invoices.AsNoTracking().OrderByDescending(i => i.UploadedAt).AsQueryable();

            if (!string.IsNullOrWhiteSpace(vendor))
                q = q.Where(i => i.VendorName != null && EF.Functions.Like(i.VendorName, $"%{vendor}%"));

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var s))
                q = q.Where(i => i.Status == s);

            var total = await q.CountAsync(cancellationToken);
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.VendorName,
                i.TotalAmount,
                i.TaxAmount,
                i.SuggestedCategory,
                i.Status,
                i.UploadedAt,
                i.ProcessedAt
            }).ToListAsync(cancellationToken);

            return Ok(new { total, page, pageSize, items });
        }

        // GET api/invoices/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetInvoice(Guid id, CancellationToken cancellationToken)
        {
            var invoice = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.Id == id)
                .Select(i => new
                {
                    i.Id,
                    i.InvoiceNumber,
                    i.IssuedDate,
                    i.VendorName,
                    i.VendorVat,
                    i.CustomerName,
                    i.TotalAmount,
                    i.TaxAmount,
                    i.Currency,
                    i.Status,
                    i.RawText,
                    i.ExtractedJson,
                    i.SuggestedCategory,
                    i.FilePath,
                    i.UploadedAt,
                    i.ProcessedAt,
                    i.IsApproved
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (invoice == null) return NotFound();

            return Ok(invoice);
        }

        // PUT api/invoices/{id}
        // body: corrected fields and optional action
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceRequest request, CancellationToken cancellationToken)
        {
            var invoice = await _db.Invoices.FindAsync(new object[] { id }, cancellationToken);
            if (invoice == null) return NotFound();

            // Apply corrections if provided
            if (request.InvoiceNumber != null) invoice.InvoiceNumber = request.InvoiceNumber;
            if (!string.IsNullOrWhiteSpace(request.VendorName)) invoice.VendorName = request.VendorName;
            if (!string.IsNullOrWhiteSpace(request.VendorVat)) invoice.VendorVat = request.VendorVat;
            if (!string.IsNullOrWhiteSpace(request.CustomerName)) invoice.CustomerName = request.CustomerName;
            if (request.TotalAmount.HasValue) invoice.TotalAmount = request.TotalAmount.Value;
            if (request.TaxAmount.HasValue) invoice.TaxAmount = request.TaxAmount;
            if (!string.IsNullOrWhiteSpace(request.SuggestedCategory)) invoice.SuggestedCategory = request.SuggestedCategory;

            if (!string.IsNullOrWhiteSpace(request.IssuedDate))
            {
                if (DateTime.TryParse(request.IssuedDate, out var dt))
                    invoice.IssuedDate = dt;
            }

            // handle actions: "approve" or "reject"
            if (!string.IsNullOrWhiteSpace(request.Action))
            {
                var action = request.Action.Trim().ToLowerInvariant();
                if (action == "approve")
                {
                    invoice.IsApproved = true;
                    invoice.Status = InvoiceStatus.Approved;
                }
                else if (action == "reject")
                {
                    invoice.IsApproved = false;
                    invoice.Status = InvoiceStatus.Rejected;
                }
            }

            invoice.ProcessedAt = DateTime.UtcNow;
            _db.Invoices.Update(invoice);
            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        // Small DTOs used by controller
        public class UpdateInvoiceRequest
        {
            public string? InvoiceNumber { get; set; }
            public string? IssuedDate { get; set; } // allow flexible formats
            public string? VendorName { get; set; }
            public string? VendorVat { get; set; }
            public string? CustomerName { get; set; }
            public decimal? TotalAmount { get; set; }
            public decimal? TaxAmount { get; set; }
            public string? SuggestedCategory { get; set; }
            public string? Action { get; set; } // "approve" or "reject"
        }
    }
}