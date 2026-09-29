using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceAnalyzerBackend.Services
{
    public class HttpAiService : IAiService
    {
        private readonly HttpClient _http;
        private readonly ILogger<HttpAiService> _logger;
        private readonly AiOptions _options;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private const string SchemaInstructions = @"
                                                    Return a single JSON object with the following fields:
                                                    - invoiceNumber (string or null)
                                                    - issuedDate (ISO date yyyy-MM-dd or null)
                                                    - vendorName (string or null)
                                                    - totalAmount (number or null)
                                                    - taxAmount (number or null)
                                                    - suggestedCategory (string or null)
                                                    - summary (short textual summary)
                                                    - issues: array of objects { description: string, severity: ""Low""|""Medium""|""High"" }
                                                    Respond with JSON only, no explanation.";

        public HttpAiService(HttpClient http, IOptions<AiOptions> options, ILogger<HttpAiService> logger)
        {
            _http = http;
            _logger = logger;
            _options = options.Value ?? new AiOptions();

            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            if (_options.TimeoutSeconds > 0)
            {
                _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            }
        }

        public async Task<AiAnalysisResult?> AnalyzeAsync(string ocrText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.Endpoint))
            {
                _logger.LogWarning("AI endpoint non configurato. Salto l'analisi IA.");
                return null;
            }

            try
            {
                var payload = new
                {
                    text = ocrText,
                    instructions = SchemaInstructions
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var res = await _http.PostAsync(_options.Endpoint, content, cancellationToken);
                var body = await res.Content.ReadAsStringAsync(cancellationToken);

                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Il servizio IA ha risposto con {Status}: {Body}", res.StatusCode, body);
                    return null;
                }

                var result = JsonSerializer.Deserialize<AiAnalysisResult>(body, _jsonOptions);
                if (result != null)
                {
                    result = result with { RawJson = body };
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chiamata ad HttpAiService.AnalyzeAsync fallita.");
                return null;
            }
        }
    }
}