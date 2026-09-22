using System;

namespace InvoiceAnalyzerBackend.Models
{
    public enum JobStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    public class InvoiceJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        public JobStatus Status { get; set; } = JobStatus.Pending;

        // progress 0-100 per feedback UI
        public int Progress { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // log sintetico dell'elaborazione / errori
        public string? ResultLog { get; set; }
    }
}