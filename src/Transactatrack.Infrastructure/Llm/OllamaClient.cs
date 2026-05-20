using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Transactatrack.Infrastructure.Llm;

public class OllamaClient
{
    private readonly HttpClient _http;
    public string Model { get; }

    public OllamaClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        Model = configuration["Ollama:Model"] ?? "llama3.2:1b";
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<TagsResponse>("/api/tags", ct);
        return response?.Models?.Select(m => m.Name).ToArray() ?? [];
    }

    /// <summary>
    /// Calls /api/chat with format=json. Uses a low-temperature deterministic profile suitable for
    /// structured-output tasks like transaction categorization.
    /// </summary>
    public async Task<string> ChatJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            stream = false,
            format = "json",
            // Qwen3 and other reasoning models default to thinking mode, which emits <think>…</think>
            // before the JSON body and breaks format=json parsing. Disable it for classification.
            think = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
            options = new
            {
                temperature = 0.0,
                top_p = 0.9,
                top_k = 20,
                repeat_penalty = 1.05,
                num_ctx = 8192,
                num_predict = 2048,
            },
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/api/chat", content, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        return result?.Message?.Content ?? string.Empty;
    }

    // Kept for backwards compatibility with any older callers — delegates to ChatJsonAsync.
    public Task<string> GenerateJsonAsync(string prompt, CancellationToken ct = default)
        => ChatJsonAsync(systemPrompt: string.Empty, userPrompt: prompt, ct);

    private sealed record TagsResponse(IReadOnlyList<ModelInfo>? Models);
    private sealed record ModelInfo(string Name);
    private sealed record ChatResponse(ChatMessage? Message);
    private sealed record ChatMessage(string? Role, string? Content);
}
