using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace InvoiceAnalyzerBackend.Services
{
    /// <summary>
    /// AnalyzerService è il servizio che estrae e valida i dati strutturati dal testo grezzo
    ///fornito dall'OCR. Usa regex keyword-oriented per trovare campi specifici e applica validazioni 
    ///logiche per rilevare anomalie.
    ///FORNITORE: Acme SRL
    /// NUMERO: FAT/2024/0001
    /// DATA: 15/09/2024
    /// TOTALE: 1.500,00 €
    /// IVA: 300,00 €
    /// Descrizione: Fornitura materie prime
    /// 
    /// </summary>

    public class AnalyzerService : IAnalyzerService
    {
        private readonly ILogger<AnalyzerService> _logger;

        public AnalyzerService(ILogger<AnalyzerService> logger)
        {
            _logger = logger;
        }

        public Task<AnalysisResult> AnalyzeAsync(string rawText, CancellationToken cancellationToken = default)
        {
            // Analisi semplice basata su regex keyword-oriented.
            var result = new AnalysisResult
            {
                RawText = rawText
            };

            try
            {
                result = result with
                {
                    VendorName = MatchFirstGroup(rawText, @"FORNITORE[:\s\-]+(.+)", RegexOptions.IgnoreCase),
                    InvoiceNumber = MatchFirstGroup(rawText, @"NUMER?O[:\s\-]+(\S+)", RegexOptions.IgnoreCase),
                    IssuedDate = ParseDate(MatchFirstGroup(rawText, @"DATA[:\s\-]+([0-9\./\-]+)", RegexOptions.IgnoreCase)),
                    TotalAmount = ParseAmount(MatchFirstGroup(rawText, @"TOTALE[:\s\-]+([0-9\.,\s]+)", RegexOptions.IgnoreCase)),
                    TaxAmount = ParseAmount(MatchFirstGroup(rawText, @"IVA[:\s\-]+([0-9\.,\s]+)", RegexOptions.IgnoreCase)),
                    SuggestedCategory = SuggestCategory(rawText)
                };

                var anomalies = new List<Anomaly>();

                // Basic validations
                if (result.IssuedDate is null)
                    anomalies.Add(new Anomaly("Data non trovata o non valida", AnomalySeverity.Low));

                if (string.IsNullOrEmpty(result.InvoiceNumber))
                    anomalies.Add(new Anomaly("Numero fattura non trovato", AnomalySeverity.Low));

                if (!result.TotalAmount.HasValue)
                    anomalies.Add(new Anomaly("Totale non riconosciuto", AnomalySeverity.Medium));

                // Example anomaly: tax vs total consistency
                if (result.TotalAmount.HasValue && result.TaxAmount.HasValue)
                {
                    var tax = result.TaxAmount.Value;
                    var total = result.TotalAmount.Value;
                    if (tax > total)
                    {
                        anomalies.Add(new Anomaly("IVA maggiore del totale", AnomalySeverity.High));
                    }
                }

                result = result with { Anomalies = anomalies };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AnalyzerService fallito; ritorno risultato minimale.");
            }

            return Task.FromResult(result);
        }

        private static string? MatchFirstGroup(string text, string pattern, RegexOptions opts = RegexOptions.None)
        {
            var m = Regex.Match(text ?? string.Empty, pattern, opts);
            if (m.Success && m.Groups.Count > 1)
                return m.Groups[1].Value.Trim();
            return null;
        }

        private static DateTime? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            s = s.Trim();
            // Support common formats: yyyy-MM-dd, dd/MM/yyyy, dd.MM.yyyy
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "dd.MM.yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out dt))
                return dt;

            return null;
        }

        private static decimal? ParseAmount(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            // Normalize: remove spaces, handle either "1.234,56" or "1234.56"
            var norm = s.Trim().Replace(" ", string.Empty);
            // If contains comma and dot, assume dot thousands, comma decimals
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
            if (t.Contains("servizi") || t.Contains("consulenza"))
                return "Servizi";
            if (t.Contains("attrezzatura") || t.Contains("macchinario") || t.Contains("equipment"))
                return "Attrezzature";
            return "Altro";
        }
    }
}