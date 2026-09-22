using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.Models;
using InvoiceAnalyzerBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvoiceAnalyzerBackend.HostedServices
{
    public class JobProcessorHostedService : BackgroundService
    {
        private readonly ILogger<JobProcessorHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public JobProcessorHostedService(ILogger<JobProcessorHostedService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("JobProcessorHostedService avviato.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var ocr = scope.ServiceProvider.GetRequiredService<IOcrService>();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerService>();

                    // Prendi fino a 5 job pending
                    var jobs = await db.InvoiceJobs
                        .Where(j => j.Status == JobStatus.Pending)
                        .OrderBy(j => j.CreatedAt)
                        .Take(5)
                        .ToListAsync(stoppingToken);

                    if (jobs.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    foreach (var job in jobs)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        job.Status = JobStatus.Running;
                        job.UpdatedAt = DateTime.UtcNow;
                        job.Progress = 10;
                        await db.SaveChangesAsync(stoppingToken);

                        try
                        {
                            var invoice = await db.Invoices.FindAsync(new object[] { job.InvoiceId }, stoppingToken);
                            if (invoice == null)
                            {
                                job.Status = JobStatus.Failed;
                                job.ResultLog = "Invoice non trovato";
                                job.Progress = 100;
                                job.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync(stoppingToken);
                                continue;
                            }

                            // Leggi file
                            if (string.IsNullOrEmpty(invoice.FilePath) || !File.Exists(invoice.FilePath))
                            {
                                job.Status = JobStatus.Failed;
                                job.ResultLog = "File fattura non trovato";
                                job.Progress = 100;
                                job.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync(stoppingToken);
                                continue;
                            }

                            await using var fs = File.OpenRead(invoice.FilePath);
                            var contentType = Path.GetExtension(invoice.FilePath) switch
                            {
                                ".pdf" => "application/pdf",
                                ".png" => "image/png",
                                ".jpg" or ".jpeg" => "image/jpeg",
                                _ => "application/octet-stream"
                            };

                            var ocrResult = await ocr.ExtractAsync(fs, Path.GetFileName(invoice.FilePath), contentType, stoppingToken);
                            job.Progress = 40;
                            await db.SaveChangesAsync(stoppingToken);

                            invoice.RawText = ocrResult.Text;

                            var analysis = await analyzer.AnalyzeAsync(ocrResult.Text, stoppingToken);
                            job.Progress = 80;

                            // Aggiorna invoice con campi estratti (se presenti)
                            invoice.InvoiceNumber = invoice.InvoiceNumber ?? analysis.InvoiceNumber;
                            invoice.IssuedDate = invoice.IssuedDate ?? analysis.IssuedDate;
                            invoice.VendorName = invoice.VendorName ?? analysis.VendorName;
                            if (analysis.TotalAmount.HasValue) invoice.TotalAmount = analysis.TotalAmount.Value;
                            if (analysis.TaxAmount.HasValue) invoice.TaxAmount = analysis.TaxAmount;
                            invoice.SuggestedCategory = analysis.SuggestedCategory;
                            invoice.ProcessedAt = DateTime.UtcNow;
                            invoice.Status = InvoiceStatus.Processed;

                            // Salva anomalie nel job log sintetico
                            job.ResultLog = string.Join(" | ", analysis.Anomalies.Select(a => $"{a.Severity}:{a.Description}"));
                            job.Progress = 100;
                            job.Status = JobStatus.Completed;
                            job.UpdatedAt = DateTime.UtcNow;

                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Job {JobId} processato per invoice {InvoiceId}", job.Id, invoice.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Errore durante l'elaborazione del job {JobId}", job.Id);
                            job.Status = JobStatus.Failed;
                            job.ResultLog = ex.Message;
                            job.Progress = 100;
                            job.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore globale nel JobProcessorHostedService");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }

            _logger.LogInformation("JobProcessorHostedService terminato.");
        }
    }
}