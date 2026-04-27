using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Flowingly_NewIntegration.IService;
using Flowingly_NewIntegration.Models;
using Microsoft.Extensions.Logging;

namespace Flowingly_NewIntegration.Services
{
    public class ExtractData : IExtractData
    {
        private readonly ILogger<ExtractData> _logger;

        public ExtractData(ILogger<ExtractData> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OutputData[]> ExtractAsync(string input)
        {
            _logger.LogInformation("Starting ExtractAsync");

            if (string.IsNullOrWhiteSpace(input))
            {
                _logger.LogWarning("Input is empty or whitespace");
                throw new InvalidOperationException("Input is empty.");
            }

            if (HasUnmatchedOpeningTags(input))
            {
                _logger.LogError("Unbalanced XML tags detected in input");
                throw new InvalidOperationException("Unbalanced XML tags detected in input.");
            }

            var matches = Regex.Matches(input, @"<expense\b.*?</expense>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matches.Count == 0)
            {
                _logger.LogInformation("No expense fragments found");
                return Array.Empty<OutputData>();
            }

            var results = new List<OutputData>();

            foreach (Match match in matches)
            {
                var xmlFragment = match.Value;
                _logger.LogDebug("Parsing fragment: {Fragment}", xmlFragment);

                XElement element;
                try
                {
                    element = XElement.Parse(xmlFragment);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse expense fragment");
                    throw new InvalidOperationException("Failed to parse expense fragment.", ex);
                }

                string? GetValue(params string[] names)
                {
                    foreach (var name in names)
                    {
                        var el = element.Elements()
                            .FirstOrDefault(e => string.Equals(NormalizeName(e.Name.LocalName), NormalizeName(name), StringComparison.OrdinalIgnoreCase));
                        if (el != null && !string.IsNullOrWhiteSpace(el.Value))
                            return el.Value.Trim();
                    }
                    return null;
                }

                var totalRaw = GetValue("total");
                if (string.IsNullOrWhiteSpace(totalRaw))
                {
                    _logger.LogError("Missing <total> element in expense fragment");
                    throw new InvalidOperationException("Missing <total> element in expense fragment.");
                }

                var costCentreRaw = GetValue("cost_centre", "costcentre", "costCentre");
                var paymentMethodRaw = GetValue("payment_method", "paymentmethod", "paymentMethod");
                var totalExclRaw = GetValue("total_excluding_tax", "totalExcludingTax", "total_excluding_tax");

                var output = new OutputData
                {
                    CostCentre = string.IsNullOrWhiteSpace(costCentreRaw) ? "UNKNOWN" : costCentreRaw,
                    PaymentMethod = string.IsNullOrWhiteSpace(paymentMethodRaw) ? "UNKNOWN" : paymentMethodRaw,
                    Total = ParseDecimal(totalRaw),
                    TotalExcludingTax = ParseDecimal(totalExclRaw)
                };

                _logger.LogInformation("Extracted expense: CostCentre={CostCentre}, Total={Total}, PaymentMethod={PaymentMethod}", output.CostCentre, output.Total, output.PaymentMethod);

                results.Add(output);
            }

            _logger.LogInformation("Completed ExtractAsync, found {Count} expense fragments", results.Count);
            return await Task.FromResult(results.ToArray());
        }

        private static bool HasUnmatchedOpeningTags(string input)
        {
            var selfClosing = new Regex(@"<([A-Za-z0-9_:.-]+)(\s[^>]*)?\/>", RegexOptions.Singleline);
            var opening = new Regex(@"<([A-Za-z0-9_:.-]+)(\s[^>]*)?>", RegexOptions.Singleline);
            var closing = new Regex(@"<\/([A-Za-z0-9_:.-]+)\s*>", RegexOptions.Singleline);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in opening.Matches(input))
            {
                var tag = m.Groups[1].Value;
                if (tag.StartsWith("?") || tag.StartsWith("!"))
                    continue;
                counts.TryGetValue(tag, out var c);
                counts[tag] = c + 1;
            }

            foreach (Match m in selfClosing.Matches(input))
            {
                var tag = m.Groups[1].Value;
                if (counts.ContainsKey(tag))
                    counts[tag] = counts[tag] - 1;
            }

            foreach (Match m in closing.Matches(input))
            {
                var tag = m.Groups[1].Value;
                if (counts.ContainsKey(tag))
                    counts[tag] = counts[tag] - 1;
                else
                    counts[tag] = -1;
            }

            return counts.Any(kv => kv.Value != 0);
        }

        private static decimal? ParseDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;

            var cleaned = s.Trim();
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ||
                decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out d))
                return d;

            var removed = cleaned.Replace(",", "").Replace(" ", "");
            if (decimal.TryParse(removed, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return d;

            return null;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return Regex.Replace(name, @"[^A-Za-z0-9]", "").ToLowerInvariant();
        }
    }
}