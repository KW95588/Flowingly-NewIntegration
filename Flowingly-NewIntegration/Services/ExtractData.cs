using Flowingly_NewIntegration.IServices;
using Flowingly_NewIntegration.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Flowingly_NewIntegration.Services
{
    public class ExtractData : IExtractData
    {
        private readonly ILogger<ExtractData> _logger;

        public ExtractData(ILogger<ExtractData> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OutputData[]> ExtractAsync(string input, decimal taxRate)
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
                var salesTax = ParseDecimal(totalRaw) * taxRate;
                var totalExclRaw = ParseDecimal(totalRaw) - salesTax;

                var output = new OutputData
                {
                    CostCentre = string.IsNullOrWhiteSpace(costCentreRaw) ? "UNKNOWN" : costCentreRaw,
                    PaymentMethod = string.IsNullOrWhiteSpace(paymentMethodRaw) ? "UNKNOWN" : paymentMethodRaw,
                    Total = ParseDecimal(totalRaw),
                    TotalExcludingTax = totalExclRaw,
                    SalesTax = salesTax
                };

                _logger.LogInformation("Extracted expense: CostCentre={CostCentre}, Total={Total}, PaymentMethod={PaymentMethod}", output.CostCentre, output.Total, output.PaymentMethod);

                results.Add(output);
            }

            _logger.LogInformation("Completed ExtractAsync, found {Count} expense fragments", results.Count);
            return await Task.FromResult(results.ToArray());
        }

        /// <summary>
        /// Async implementation that extracts all detected tags (including XML content),
        /// </summary>
        public async Task<string> ExtractDataFromTags(string inputString, decimal taxRate)
        {
            _logger.LogInformation("Starting ExtractDataFromTags");

            if (string.IsNullOrWhiteSpace(inputString))
            {
                _logger.LogWarning("inputString is empty or whitespace");
                throw new InvalidOperationException("Input is empty.");
            }

            if (HasUnmatchedOpeningTags(inputString))
            {
                _logger.LogError("Unbalanced XML tags detected in input for ExtractDataFromTags");
                throw new InvalidOperationException("Unbalanced XML tags detected in input.");
            }

            var detected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Helper to map raw tag name to output JSON key (preserves expected names like "cost_centre")
            static string KeyForJson(string rawTag)
            {
                var norm = Regex.Replace(rawTag ?? string.Empty, @"[^A-Za-z0-9]", "").ToLowerInvariant();
                if (norm == "costcentre") return "cost_centre";
                if (norm == "paymentmethod") return "payment_method";
                return norm;
            }

            // Normalize extracted values: remove control chars and collapse whitespace/newlines to single space.
            static string NormalizeValue(string? s)
            {
                if (string.IsNullOrEmpty(s))
                    return string.Empty;
                // Replace control characters with space then collapse whitespace
                var cleaned = Regex.Replace(s, @"[\x00-\x1F]+", " ");
                cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
                return cleaned;
            }

            // 1) Attempt to parse any XML fragments first — this reliably discovers nested tags like <total>
            var xmlFragmentMatches = Regex.Matches(inputString, @"<([A-Za-z0-9_:.-]+)\b[^>]*>.*?</\1>", RegexOptions.Singleline);
            foreach (Match frag in xmlFragmentMatches)
            {
                var fragment = frag.Value;
                try
                {
                    var root = XElement.Parse(fragment);
                    // collect leaf nodes only
                    foreach (var leaf in root.Descendants().Where(e => !e.HasElements))
                    {
                        var key = KeyForJson(leaf.Name.LocalName);
                        // skip the container root name (e.g. "expense") if it somehow appears as a leaf
                        if (string.Equals(key, KeyForJson(root.Name.LocalName), StringComparison.OrdinalIgnoreCase))
                            continue;
                        var value = NormalizeValue(leaf.Value);
                        if (!string.IsNullOrWhiteSpace(value) && !detected.ContainsKey(key))
                            detected[key] = value;
                    }
                }
                catch
                {
                    // ignore parse failures for fragments and continue — we'll still try other extraction methods
                }
            }

            // 2) Also extract any simple tag pairs across the whole input (covers tags not part of valid XML fragments)
            var tagPairs = Regex.Matches(inputString, @"<([A-Za-z0-9_:.-]+)(?:\s[^>]*)?>(.*?)</\1>", RegexOptions.Singleline);
            foreach (Match m in tagPairs)
            {
                var rawTag = m.Groups[1].Value;
                var rawInner = m.Groups[2].Value ?? string.Empty;
                // strip any nested tags then normalize value
                var innerText = Regex.Replace(rawInner, @"<[^>]+>", "");
                var value = NormalizeValue(innerText);
                var key = KeyForJson(rawTag);
                // Skip container root keys such as "expense"
                if (string.Equals(key, "expense", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!detected.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
                    detected[key] = value;
            }

            // If still no detected tags, try to parse whole input as XML (fallback)
            if (detected.Count == 0)
            {
                try
                {
                    var root = XElement.Parse(inputString);
                    foreach (var el in root.Descendants().Where(e => !e.HasElements))
                    {
                        var key = KeyForJson(el.Name.LocalName);
                        if (string.Equals(key, KeyForJson(root.Name.LocalName), StringComparison.OrdinalIgnoreCase))
                            continue;
                        var value = NormalizeValue(el.Value);
                        if (!detected.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
                            detected[key] = value;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // Ensure <total> exists
            var totalKey = KeyForJson("total");
            if (!detected.TryGetValue(totalKey, out var totalRaw) || string.IsNullOrWhiteSpace(totalRaw))
            {
                _logger.LogError("Missing <total> element in ExtractDataFromTags input");
                throw new InvalidOperationException("Missing <total> element in input.");
            }

            var totalValue = ParseDecimal(totalRaw);
            if (totalValue == null)
            {
                _logger.LogError("Unable to parse total value: {TotalRaw}", totalRaw);
                throw new InvalidOperationException("Invalid total value in input.");
            }

            var salesTax = totalValue.Value * taxRate;
            var totalExcludingTax = totalValue.Value - salesTax;

            // Ensure cost_centre key exists in output (default UNKNOWN)
            if (!detected.ContainsKey("cost_centre"))
            {
                if (detected.TryGetValue("costcentre", out var ccAlt) && !string.IsNullOrWhiteSpace(ccAlt))
                    detected["cost_centre"] = ccAlt;
                else
                    detected["cost_centre"] = "UNKNOWN";
            }

            // Build output dictionary: include all detected tags and computed fields
            var output = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in detected)
            {
                if (string.Equals(kv.Key, totalKey, StringComparison.OrdinalIgnoreCase))
                {
                    output["total"] = totalValue.Value;
                }
                else
                {
                    output[kv.Key] = kv.Value;
                }
            }

            // Add computed fields (override if present)
            output["sales_tax"] = salesTax;
            output["total_excluding_tax"] = totalExcludingTax;

            var json = JsonSerializer.Serialize(output);

            _logger.LogInformation("Completed ExtractDataFromTags");
            return await Task.FromResult(json);
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

