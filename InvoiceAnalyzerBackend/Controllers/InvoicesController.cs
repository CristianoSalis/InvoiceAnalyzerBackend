using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvoiceAnalyzerBackend.Controllers
{
    /// <summary>
    /// Controller API REST per la gestione delle fatture e dei relativi job di elaborazione.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class InvoicesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<InvoicesController> _logger;
        private readonly IHostEnvironment _env;

        public InvoicesController(
            AppDbContext db,
            ILogger<InvoicesController> logger,
            IHostEnvironment env)
        {
            _db = db;
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// Carica un file PDF di una fattura e avvia un job di analisi in background.
        /// </summary>
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Nessun file fornito." });

            // Validazione estensione file (supporto PDF e HTML di test)
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".html", ".htm" };
            if (!allowedExtensions.Contains(ext))
            {
                return BadRequest(new { error = "Formato file non supportato. Caricare un PDF o HTML." });
            }

            // Assicura l'esistenza della cartella uploads
            var uploadsDir = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploadsDir);

            var uniqueFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsDir, uniqueFileName);

            await using (var fs = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(fs, cancellationToken);
            }

            // Crea il record Invoice
            var invoice = new Invoice
            {
                FilePath = filePath,
                Status = InvoiceStatus.Uploaded,
                UploadedAt = DateTime.UtcNow
            };

            await _db.Invoices.AddAsync(invoice, cancellationToken);

            // Crea il Job in stato Pending per il Background Processor
            var job = new InvoiceJob
            {
                Invoice = invoice,
                Status = JobStatus.Pending,
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _db.InvoiceJobs.AddAsync(job, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Caricata fattura ID {InvoiceId} con Job ID {JobId}", invoice.Id, job.Id);

            var result = new
            {
                invoiceId = invoice.Id,
                jobId = job.Id
            };

            return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, result);
        }

        /// <summary>
        /// Recupera lo stato di avanzamento di un Job di analisi.
        /// </summary>
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

        /// <summary>
        /// Recupera la lista paginata e filtrabile delle fatture.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? vendor,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
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

        /// <summary>
        /// Dettaglio singolo di una fattura tramite il suo ID.
        /// </summary>
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

        /// <summary>
        /// Aggiorna o approva/rifiuta i dati estrapolati di una fattura.
        /// </summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceRequest request, CancellationToken cancellationToken)
        {
            var invoice = await _db.Invoices.FindAsync(new object[] { id }, cancellationToken);
            if (invoice == null) return NotFound();

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

        public class UpdateInvoiceRequest
        {
            public string? InvoiceNumber { get; set; }
            public string? IssuedDate { get; set; }
            public string? VendorName { get; set; }
            public string? VendorVat { get; set; }
            public string? CustomerName { get; set; }
            public decimal? TotalAmount { get; set; }
            public decimal? TaxAmount { get; set; }
            public string? SuggestedCategory { get; set; }
            public string? Action { get; set; }
        }
    }
}