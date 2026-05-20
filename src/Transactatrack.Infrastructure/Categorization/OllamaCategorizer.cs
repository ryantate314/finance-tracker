using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Transactatrack.Application.Categorization;
using Transactatrack.Domain.Entities;
using Transactatrack.Infrastructure.Llm;

namespace Transactatrack.Infrastructure.Categorization;

public class OllamaCategorizer : IOllamaCategorizer
{
    // One Ollama call at a time — single GPU/CPU backend.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private const string SystemPrompt =
        "You are a deterministic personal-finance transaction classifier. " +
        "For every transaction you receive, output a JSON object that maps the transaction key " +
        "(tx1, tx2, ...) to an object with categoryId (integer, required), subCategoryId " +
        "(integer, optional), and confidence (number 0.0-1.0, required). " +
        "Rules: " +
        "(1) You MUST classify every transaction — never omit one. If unsure, pick the closest " +
        "category and use a low confidence (0.3-0.5). " +
        "(2) categoryId must be one of the listed category numbers. " +
        "(3) subCategoryId, if provided, must be a sub-category number listed under the chosen " +
        "category — never from a different category. Omit subCategoryId if no sub-category clearly fits. " +
        "(4) Positive amounts are credits (income, refunds, transfers in). Negative amounts are debits. " +
        "(5) Output JSON only — no prose, no markdown.";

    private readonly OllamaClient _ollama;
    private readonly ILogger<OllamaCategorizer> _logger;

    public OllamaCategorizer(OllamaClient ollama, ILogger<OllamaCategorizer> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    public async Task<IDictionary<Guid, LlmCategorizationResult>> SuggestAsync(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCategory> subCategories,
        CancellationToken ct)
    {
        if (transactions.Count == 0 || categories.Count == 0)
            return new Dictionary<Guid, LlmCategorizationResult>();

        // Map small integer IDs to category Guids to avoid hallucinated GUIDs in prompts.
        var idToCategory = categories
            .Select((c, i) => (Index: i + 1, Category: c))
            .ToDictionary(x => x.Index, x => x.Category);
        var subsByCategory = subCategories.GroupBy(s => s.CategoryId).ToDictionary(g => g.Key, g => g.ToList());

        // Sub-category int IDs are independent of category int IDs.
        var idToSubCategory = subCategories
            .Select((s, i) => (Index: i + 1, Sub: s))
            .ToDictionary(x => x.Index, x => x.Sub);
        var subGuidToIndex = idToSubCategory.ToDictionary(x => x.Value.Id, x => x.Key);

        var sb = new StringBuilder();
        foreach (var (catIdx, cat) in idToCategory)
        {
            sb.Append("  ").Append(catIdx).Append(": ").Append(cat.Name);
            if (subsByCategory.TryGetValue(cat.Id, out var subs) && subs.Count > 0)
            {
                var subList = string.Join(", ", subs.Select(s => $"{subGuidToIndex[s.Id]}={s.Name}"));
                sb.Append("  [subs: ").Append(subList).Append(']');
            }
            sb.AppendLine();
        }
        var categoryList = sb.ToString().TrimEnd();

        var txLines = string.Join("\n", transactions.Select((t, i) =>
        {
            string merchant = !string.IsNullOrWhiteSpace(t.Merchant) ? t.Merchant! : "(none)";
            string sign = t.Amount >= 0 ? "+" : "";
            return $"  tx{i + 1}: amount={sign}{t.Amount:F2} merchant=\"{merchant}\" desc=\"{t.Description}\"";
        }));

        var exampleJson = """{"tx1":{"categoryId":1,"subCategoryId":2,"confidence":0.95},"tx2":{"categoryId":3,"confidence":0.4}}""";
        var userPrompt = $"""
Categories:
{categoryList}

Transactions:
{txLines}

Return a JSON object. Every tx key above MUST appear exactly once. Example shape:
{exampleJson}
""";

        await Gate.WaitAsync(ct);
        string raw;
        try
        {
            raw = await _ollama.ChatJsonAsync(SystemPrompt, userPrompt, ct);
        }
        finally
        {
            Gate.Release();
        }

        var results = new Dictionary<Guid, LlmCategorizationResult>();
        if (string.IsNullOrWhiteSpace(raw)) return results;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;

            // Some models wrap the answer in {"transactions": {...}} or similar — try to unwrap a single nested object.
            if (root.ValueKind == JsonValueKind.Object && !ContainsTxKey(root))
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object && ContainsTxKey(prop.Value))
                    {
                        root = prop.Value;
                        break;
                    }
                }
            }

            foreach (var prop in root.EnumerateObject())
            {
                // Extract tx index from key like "tx3"
                if (!prop.Name.StartsWith("tx", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(prop.Name[2..], out var txIdx)) continue;
                if (txIdx < 1 || txIdx > transactions.Count) continue;

                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (!prop.Value.TryGetProperty("categoryId", out var catIdEl)) continue;
                if (!TryReadInt(catIdEl, out var catIntId)) continue;
                if (!idToCategory.TryGetValue(catIntId, out var category)) continue;

                Guid? subCategoryId = null;
                if (prop.Value.TryGetProperty("subCategoryId", out var subIdEl)
                    && TryReadInt(subIdEl, out var subIntId)
                    && idToSubCategory.TryGetValue(subIntId, out var sub)
                    && sub.CategoryId == category.Id)
                {
                    subCategoryId = sub.Id;
                }

                decimal confidence = 0;
                if (prop.Value.TryGetProperty("confidence", out var confEl))
                {
                    if (confEl.ValueKind == JsonValueKind.Number)
                        confEl.TryGetDecimal(out confidence);
                    else if (confEl.ValueKind == JsonValueKind.String
                             && decimal.TryParse(confEl.GetString(), out var parsed))
                        confidence = parsed;
                }

                confidence = Math.Clamp(confidence, 0m, 1m);

                var txGuid = transactions[txIdx - 1].Id;
                results[txGuid] = new LlmCategorizationResult(category.Id, subCategoryId, Math.Round(confidence, 2), _ollama.Model);
            }
        }
        catch (JsonException ex)
        {
            var truncated = raw.Length > 500 ? raw[..500] : raw;
            _logger.LogWarning(ex, "Failed to parse Ollama JSON response. Raw (truncated): {Raw}", truncated);
        }

        return results;
    }

    private static bool ContainsTxKey(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.StartsWith("tx", StringComparison.OrdinalIgnoreCase)
                && p.Name.Length > 2
                && char.IsDigit(p.Name[2]))
                return true;
        return false;
    }

    private static bool TryReadInt(JsonElement el, out int value)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetInt32(out value);
            case JsonValueKind.String:
                return int.TryParse(el.GetString(), out value);
            default:
                value = 0;
                return false;
        }
    }
}
