using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InvoiceAnalyzerBackend.Services
{
    public enum IssueSeverity
    {
        Low,
        Medium,
        High
    }

    public record AiIssue(string Description, IssueSeverity Severity);

    public record AiAnalysisResult
    {
        public string? InvoiceNumber { get; init; }
        public DateTime? IssuedDate { get; init; }
        public string? VendorName { get; init; }
        public decimal? TotalAmount { get; init; }
        public decimal? TaxAmount { get; init; }
        public string? SuggestedCategory { get; init; }
        public string? Summary { get; init; }
        public List<AiIssue> Issues { get; init; } = new();
        public string? RawJson { get; init; }
    }

    public interface IAiService
    {
        /// <summary>
        /// Invia il testo OCR al servizio LLM/Document Intelligence e riceve un risultato strutturato.
        /// Ritorna null in caso di errore o se il servizio non è configurato.
        /// </summary>
        Task<AiAnalysisResult?> AnalyzeAsync(string ocrText, CancellationToken cancellationToken = default);
    }
}