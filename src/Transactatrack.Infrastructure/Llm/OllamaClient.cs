using System.Net.Http.Json;

namespace Transactatrack.Infrastructure.Llm;

public class OllamaClient
{
    private readonly HttpClient _http;

    public OllamaClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<TagsResponse>("/api/tags", ct);
        return response?.Models?.Select(m => m.Name).ToArray() ?? [];
    }

    private sealed record TagsResponse(IReadOnlyList<ModelInfo>? Models);
    private sealed record ModelInfo(string Name);
}
