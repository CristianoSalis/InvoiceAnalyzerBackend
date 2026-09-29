namespace InvoiceAnalyzerBackend.Services
{
    public class AiOptions
    {
        public string? Endpoint { get; set; }
        public string? ApiKey { get; set; }
        public int TimeoutSeconds { get; set; } = 60;
    }
}