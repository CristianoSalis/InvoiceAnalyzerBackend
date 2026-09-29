using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InvoiceAnalyzerBackend.Services
{
    public class AnalyzerService : IAnalyzerService
    {
        private readonly ILogger<AnalyzerService> _logger;
        private readonly IAiService _ai;

        public AnalyzerService(ILogger<AnalyzerService> logger, IAiService ai)
        {
            _logger = logger;
            _ai = ai;
        }

        public async Task<AnalysisResult> AnalyzeAsync(string rawText, CancellationToken cancellationToken = default)
        {
            var result = new AnalysisResult { RawText = rawText };

            try
            {
                // 1. Tenta l'analisi con l'I.A.
                var aiRes = await _ai.AnalyzeAsync(rawText, cancellationToken);

                if (aiRes != null)
                {
                    _logger.LogInformation("Analisi I.A. completata con successo per il testo fornito.");

                    var anomalies = new List<Anomaly>();

                    // Mappa gli errori/anomalie restituite dall'I.A.
                    if (aiRes.Issues != null)
                    {
                        foreach (var issue in aiRes.Issues)
                        {
                            var sev = issue.Severity switch
                            {
                                IssueSeverity.High => AnomalySeverity.High,
                                IssueSeverity.Medium => AnomalySeverity.Medium,
                                _ => AnomalySeverity.Low
                            };
                            anomalies.Add(new Anomaly(issue.Description, sev));
                        }
                    }

                    // Valida l'aritmetica di base sui dati restituiti dall'I.A.
                    if (aiRes.TotalAmount.HasValue && aiRes.TaxAmount.HasValue && aiRes.TaxAmount > aiRes.TotalAmount)
                    {
                        anomalies.Add(new Anomaly("IVA calcolata maggiore del totale dell'importo.", AnomalySeverity.High));
                    }

                    result = result with
                    {
                        InvoiceNumber = aiRes.InvoiceNumber,
                        IssuedDate = aiRes.IssuedDate,
                        VendorName = aiRes.VendorName,
                        TotalAmount = aiRes.TotalAmount,
                        TaxAmount = aiRes.TaxAmount,
                        SuggestedCategory = string.IsNullOrWhiteSpace(aiRes.SuggestedCategory) ? SuggestCategory(rawText) : aiRes.SuggestedCategory,
                        Anomalies = anomalies
                    };

                    // Se l'I.A. ha estratto i dati principali, ritorna il risultato
                    if (!string.IsNullOrEmpty(result.InvoiceNumber) || result.TotalAmount.HasValue)
                    {
                        return result;
                    }

                    _logger.LogWarning("Risultato I.A. incompleto o vuoto. Esecuzione del Fallback su Regex...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante l'analisi I.A.; esecuzione del Fallback Regex.");
            }

            // 2. Fallback: Analisi basata su espressioni regolari
            return BasicRegexAnalyze(rawText);
        }

        private AnalysisResult BasicRegexAnalyze(string rawText)
        {
            _logger.LogInformation("Esecuzione analisi di fallback tramite Regex...");

            var result = new AnalysisResult { RawText = rawText };

            try
            {
                result = result with
                {
                    // Regex aggiornate e piú flessibili per gestire testi uniti senza spazi
                    VendorName = MatchFirstGroup(rawText, @"^(.*?)(?:P\.?IVA|VAT|Via|InvoiceNumber|Number)", RegexOptions.IgnoreCase),
                    InvoiceNumber = MatchFirstGroup(rawText, @"(?:Invoice\s*Number|InvoiceNumber|Number)[:\s\-]*([A-Z0-9\-]+)", RegexOptions.IgnoreCase),
                    IssuedDate = ParseDate(MatchFirstGroup(rawText, @"Date[:\s\-]*([0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{2}[\/\.-][0-9]{2}[\/\.-][0-9]{4})", RegexOptions.IgnoreCase)),
                    TotalAmount = ParseAmount(MatchFirstGroup(rawText, @"(?<!Sub)Total[:\s\-]*([0-9\.,]+)", RegexOptions.IgnoreCase)),
                    TaxAmount = ParseAmount(MatchFirstGroup(rawText, @"VAT\s*\d+%\s*([0-9\.,]+)", RegexOptions.IgnoreCase)),
                    SuggestedCategory = SuggestCategory(rawText)
                };

                var anomalies = new List<Anomaly>();
                if (result.IssuedDate is null) anomalies.Add(new Anomaly("Data non trovata o non valida", AnomalySeverity.Low));
                if (string.IsNullOrEmpty(result.InvoiceNumber)) anomalies.Add(new Anomaly("Numero fattura non trovato", AnomalySeverity.Low));
                if (!result.TotalAmount.HasValue) anomalies.Add(new Anomaly("Totale non riconosciuto", AnomalySeverity.Medium));

                result = result with { Anomalies = anomalies };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'esecuzione di BasicRegexAnalyze.");
            }

            return result;
        }

        private static string? MatchFirstGroup(string text, string pattern, RegexOptions opts = RegexOptions.None)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, pattern, opts);
            if (m.Success && m.Groups.Count > 1)
                return m.Groups[1].Value.Trim();
            return null;
        }

        private static DateTime? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            s = s.Trim();
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "dd.MM.yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out dt))
                return dt;

            return null;
        }

        private static decimal? ParseAmount(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var norm = s.Trim().Replace(" ", string.Empty);
            if (norm.Contains('.') && norm.Contains(','))
            {
                norm = norm.Replace(".", string.Empty).Replace(',', '.');
            }
            else if (norm.Contains(','))
            {
                norm = norm.Replace(',', '.');
            }

            if (decimal.TryParse(norm, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return d;

            return null;
        }

        private static string? SuggestCategory(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var t = text.ToLowerInvariant();
            if (t.Contains("materie prime") || t.Contains("materiale") || t.Contains("fornitura"))
                return "Materie Prime";
            if (t.Contains("servizi") || t.Contains("consulenza") || t.Contains("consulting"))
                return "Servizi";
            if (t.Contains("attrezzatura") || t.Contains("macchinario") || t.Contains("license"))
                return "Attrezzature";
            return "Altro";
        }
    }
}