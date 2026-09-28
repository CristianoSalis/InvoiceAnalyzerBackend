using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace InvoiceAnalyzerBackend.Services
{
    public class MockOcrService : IOcrService
    {
        private readonly ILogger<MockOcrService> _logger;



        /// <summary>
        /// MockOcrService è un'implementazione fittizia (mock) del servizio OCR usata per lo sviluppo e il testing. 
        /// Simula l'estrazione di testo da file PDF e immagini senza dipendere da servizi OCR reali (come Google Vision o Tesseract).
        /// </summary>
        /// <param name="logger"></param>

        public MockOcrService(ILogger<MockOcrService> logger)
        {
            _logger = logger;
        }

        public async Task<OcrResult> ExtractAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            // Simula latenza minima
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            try
            {
                // Se è un PDF, prova a estrarre testo reale con PdfPig
                if ((!string.IsNullOrEmpty(contentType) && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!fileStream.CanSeek)
                    {
                        var ms = new MemoryStream();
                        await fileStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                        ms.Position = 0;
                        fileStream = ms;
                    }

                    fileStream.Position = 0;
                    var sb = new StringBuilder();

                    using (var doc = PdfDocument.Open(fileStream))
                    {
                        foreach (var page in doc.GetPages())
                        {
                            var text = page.Text;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                sb.AppendLine(text);
                            }
                        }
                    }

                    var extracted = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        return new OcrResult
                        {
                            Text = extracted,
                            Regions = Array.Empty<TextRegion>()
                        };
                    }

                    _logger.LogInformation("PdfPig non ha trovato testo significativo; userò il mock testuale.");
                }

                // Per immagini o PDF senza testo: ritorna mock strutturato
                return CreateImageMock(fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante OCR; ritorno mock minimo.");
                return CreateImageMock(fileName);
            }
        }


        /// <summary>
        /// Nome fornitore: ACME SRL
        ///Data: 2026-09-01
        ///Numero: INV-2026-001
        ///Importi: 1000.00 EUR(base) + 220.00 EUR(IVA) = 1220.00 EUR
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static OcrResult CreateImageMock(string? fileName)
        {
            var sample = new StringBuilder();
            sample.AppendLine("FORNITORE: ACME SRL");
            sample.AppendLine("P.IVA: 01234567890");
            sample.AppendLine("DATA: 2026-09-01");
            sample.AppendLine("NUMERO: INV-2026-001");
            sample.AppendLine("DESCRIZIONE: Fornitura materie prime");
            sample.AppendLine("IMPOGNABILE: 1000.00 EUR");
            sample.AppendLine("IVA: 220.00 EUR");
            sample.AppendLine("TOTALE: 1220.00 EUR");

            if (!string.IsNullOrEmpty(fileName) && fileName.Contains("services", StringComparison.OrdinalIgnoreCase))
            {
                sample.AppendLine("DESCRIZIONE: Servizi di consulenza");
            }

            return new OcrResult
            {
                Text = sample.ToString(),
                Regions = Array.Empty<TextRegion>()
            };
        }
    }
}