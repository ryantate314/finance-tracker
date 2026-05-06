using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categorization;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Categorization;

/// <summary>
/// Uses a deterministic stub IOllamaCategorizer so no real Ollama is needed.
/// </summary>
public class LlmTests : IClassFixture<LlmTestFactory>
{
    private readonly LlmTestFactory _factory;
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv");

    public LlmTests(LlmTestFactory factory) => _factory = factory;

    private async Task<(HttpClient client, Guid accountId, Guid categoryId)> SetupAsync()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;

        var client = _factory.CreateClientWithFamily(family.Id);

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Shopping"));
        var category = (await catResp.Content.ReadFromJsonAsync<CategoryDto>(IntegrationTestFactory.JsonOpts))!;

        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;
        var acctResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase Card", "Chase", AccountType.CreditCard, "Chase"));
        var account = (await acctResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        // Tell the stub which category to return
        _factory.Stub.NextCategoryId = category.Id;

        return (client, account.Id, category.Id);
    }

    private static MultipartFormDataContent BuildUpload(Guid accountId, Stream csv)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(accountId.ToString()), "accountId");
        var fileContent = new StreamContent(csv);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "ChaseSample.csv");
        return form;
    }

    [Fact]
    public async Task SuggestLlm_EnqueuesWork_StatusEventuallyComplete()
    {
        var (client, accountId, categoryId) = await SetupAsync();

        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream);
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        var batchId = preview.BatchId;

        var suggestResp = await client.PostAsync($"/api/imports/{batchId}/suggest-llm", null);
        Assert.Equal(HttpStatusCode.Accepted, suggestResp.StatusCode);

        // Poll until Complete (stub is synchronous so it should finish quickly)
        ImportBatchDto? batch = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var detailResp = await client.GetFromJsonAsync<ImportBatchDetailDto>(
                $"/api/imports/{batchId}", IntegrationTestFactory.JsonOpts);
            batch = detailResp?.Batch;
            if (batch?.LlmStatus == LlmCategorizationStatus.Complete) break;
        }

        Assert.Equal(LlmCategorizationStatus.Complete, batch?.LlmStatus);
        Assert.True(batch?.LlmRowsDone > 0);
    }

    [Fact]
    public async Task SuggestLlm_WhileRunning_Returns409()
    {
        var (client, accountId, _) = await SetupAsync();

        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream);
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        var batchId = preview.BatchId;

        // Pause the stub so it's stuck in Running state
        _factory.Stub.PauseProcessing = true;
        await client.PostAsync($"/api/imports/{batchId}/suggest-llm", null);

        // Second call while running → 409
        var resp2 = await client.PostAsync($"/api/imports/{batchId}/suggest-llm", null);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);

        _factory.Stub.PauseProcessing = false;
    }

    private record ImportBatchDetailDto(ImportBatchDto Batch, List<ImportPreviewRowDto> Transactions);
}

public class LlmTestFactory : IntegrationTestFactory
{
    public StubOllamaCategorizer Stub { get; } = new();

    public LlmTestFactory()
    {
        OllamaCategorizerStub = Stub;
    }
}

public class StubOllamaCategorizer : IOllamaCategorizer
{
    public Guid NextCategoryId { get; set; }
    public bool PauseProcessing { get; set; }

    public async Task<IDictionary<Guid, LlmCategorizationResult>> SuggestAsync(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCategory> subCategories,
        CancellationToken ct)
    {
        while (PauseProcessing)
            await Task.Delay(50, ct);

        var result = new Dictionary<Guid, LlmCategorizationResult>();
        if (NextCategoryId == Guid.Empty) return result;

        foreach (var tx in transactions)
            result[tx.Id] = new LlmCategorizationResult(NextCategoryId, null, 0.9m, "stub-model");

        return result;
    }
}
