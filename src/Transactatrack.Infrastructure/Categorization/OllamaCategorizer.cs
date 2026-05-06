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
        CancellationToken ct)
    {
        if (transactions.Count == 0 || categories.Count == 0)
            return new Dictionary<Guid, LlmCategorizationResult>();

        // Map small integer IDs to category Guids to avoid hallucinated GUIDs in prompts.
        var idToCategory = categories
            .Select((c, i) => (Index: i + 1, Category: c))
            .ToDictionary(x => x.Index, x => x.Category);
        var guidToIndex = idToCategory.ToDictionary(x => x.Value.Id, x => x.Key);

        var categoryList = string.Join("\n", idToCategory.Select(x => $"  {x.Key}: {x.Value.Name}"));
        var txLines = string.Join("\n", transactions.Select((t, i) =>
            $"  tx{i + 1} | {t.Date:yyyy-MM-dd} | {t.Amount:F2} | {t.Merchant ?? t.Description}"));

        var exampleJson = """{"tx1":{"categoryId":3,"confidence":0.9},"tx2":{"categoryId":1,"confidence":0.7}}""";
        var prompt = $"""
You are a personal finance categorization assistant. Given a list of bank transactions and available categories, assign the best category to each transaction.

Categories:
{categoryList}

Transactions:
{txLines}

Respond with a JSON object mapping transaction keys to their category id and confidence score (0.0-1.0).
Example: {exampleJson}

Only include transactions you are confident about. Omit any transaction if unsure.
""";

        await Gate.WaitAsync(ct);
        string raw;
        try
        {
            raw = await _ollama.GenerateJsonAsync(prompt, ct);
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
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Extract tx index from key like "tx3"
                if (!prop.Name.StartsWith("tx")) continue;
                if (!int.TryParse(prop.Name[2..], out var txIdx)) continue;
                if (txIdx < 1 || txIdx > transactions.Count) continue;

                if (!prop.Value.TryGetProperty("categoryId", out var catIdEl)) continue;
                if (!catIdEl.TryGetInt32(out var catIntId)) continue;
                if (!idToCategory.TryGetValue(catIntId, out var category)) continue;

                decimal confidence = 0;
                if (prop.Value.TryGetProperty("confidence", out var confEl))
                    confEl.TryGetDecimal(out confidence);

                if (confidence < 0 || confidence > 1) confidence = Math.Clamp(confidence, 0, 1);

                var txGuid = transactions[txIdx - 1].Id;
                results[txGuid] = new LlmCategorizationResult(category.Id, Math.Round(confidence, 2), _ollama.Model);
            }
        }
        catch (JsonException ex)
        {
            var truncated = raw.Length > 500 ? raw[..500] : raw;
            _logger.LogWarning(ex, "Failed to parse Ollama JSON response. Raw (truncated): {Raw}", truncated);
        }

        return results;
    }
}
