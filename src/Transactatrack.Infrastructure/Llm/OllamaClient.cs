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

    public async Task<string> GenerateJsonAsync(string prompt, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            prompt,
            stream = false,
            format = "json"
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/api/generate", content, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: ct);
        return result?.Response ?? string.Empty;
    }

    private sealed record TagsResponse(IReadOnlyList<ModelInfo>? Models);
    private sealed record ModelInfo(string Name);
    private sealed record GenerateResponse(string Response);
}
