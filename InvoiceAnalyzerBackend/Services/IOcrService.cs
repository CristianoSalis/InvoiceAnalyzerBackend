using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InvoiceAnalyzerBackend.Services
{
    public record TextRegion(int Page, string Text, double X, double Y, double Width, double Height);

    public class OcrResult
    {
        public string Text { get; init; } = string.Empty;
        public IReadOnlyList<TextRegion> Regions { get; init; } = Array.Empty<TextRegion>();
    }

    public interface IOcrService
    {
        /// <summary>
        /// Estrae testo e regioni (opzionali) dal file fornito.
        /// </summary>
        /// <param name="fileStream">Stream del file (posizionato all'inizio).</param>
        /// <param name="fileName">Nome del file caricato (può aiutare il mock a decidere).</param>
        /// <param name="contentType">MIME type (es. application/pdf, image/png).</param>
        /// <param name="cancellationToken">Token di cancellazione.</param>
        Task<OcrResult> ExtractAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    }
}