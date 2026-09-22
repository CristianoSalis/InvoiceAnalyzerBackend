using System;

namespace InvoiceAnalyzerBackend.Models
{
    public enum InvoiceStatus
    {
        Uploaded,
        Processing,
        Processed,
        Approved,
        Rejected
    }

    public class Invoice
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string? InvoiceNumber { get; set; }
        public DateTime? IssuedDate { get; set; }

        public string? VendorName { get; set; }
        public string? VendorVat { get; set; }

        public string? CustomerName { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public string? Currency { get; set; } = "EUR";

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Uploaded;

        public string? RawText { get; set; }
        public string? ExtractedJson { get; set; }

        // percorso relativo su disco (es. wwwroot/uploads/...)
        public string? FilePath { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }

        public string? SuggestedCategory { get; set; }
        public bool IsApproved { get; set; } = false;
    }
}