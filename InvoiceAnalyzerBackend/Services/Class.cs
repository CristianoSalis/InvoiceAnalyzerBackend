using System.Threading;
using System.Threading.Tasks;

namespace InvoiceAnalyzerBackend.Services
{
    /// <summary>
    /// Implementazione di fallback (Null Object Pattern) usata quando non è configurato un Endpoint IA.
    /// </summary>
    public class NullAiService : IAiService
    {
        public Task<AiAnalysisResult?> AnalyzeAsync(string ocrText, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AiAnalysisResult?>(null);
        }
    }
}