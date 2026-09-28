using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InvoiceAnalyzerBackend.Services
{
    public enum AnomalySeverity
    {
        Low,
        Medium,
        High
    }

    public record Anomaly(string Description, AnomalySeverity Severity);

    public record AnalysisResult
    {
        public string? InvoiceNumber { get; init; }
        public DateTime? IssuedDate { get; init; }
        public string? VendorName { get; init; }
        public decimal? TotalAmount { get; init; }
        public decimal? TaxAmount { get; init; }
        public string? SuggestedCategory { get; init; }
        public IReadOnlyList<Anomaly> Anomalies { get; init; } = Array.Empty<Anomaly>();
        public string RawText { get; init; } = string.Empty;
    }



    public interface IAnalyzerService
    {
        /// <summary>
        /// Analizza il testo OCR e ritorna risultati strutturati + anomalie/suggerimenti.
        /// </summary>
        Task<AnalysisResult> AnalyzeAsync(string rawText, CancellationToken cancellationToken = default);
    }
}