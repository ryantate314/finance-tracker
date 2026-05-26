using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Categorization;

public class SystemTransferCategoryTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv");

    public SystemTransferCategoryTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task CreatingFamily_AutoSeedsTransferCategory()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        familyResp.EnsureSuccessStatusCode();
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;

        var client = _factory.CreateClientWithFamily(family.Id);
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", IntegrationTestFactory.JsonOpts))!;

        Assert.Single(categories);
        Assert.Equal("Transfer", categories[0].Name);
        Assert.Equal(CategoryKind.Transfer, categories[0].Kind);
    }

    [Fact]
    public async Task DeletingSystemTransferCategory_Returns409()
    {
        var client = await NewFamilyClient();
        var transfer = await GetTransferCategory(client);

        var resp = await client.DeleteAsync($"/api/categories/{transfer.Id}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task RenamingSystemTransferCategory_Returns409()
    {
        var client = await NewFamilyClient();
        var transfer = await GetTransferCategory(client);

        var resp = await client.PutAsJsonAsync($"/api/categories/{transfer.Id}", new UpdateCategoryRequest("Renamed"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PatchToTransferCategory_SetsIsTransferTrue()
    {
        var (client, txId, transferId, userCategoryId) = await SetupWithTransaction();

        var resp = await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(transferId, null));
        resp.EnsureSuccessStatusCode();

        var updated = (await resp.Content.ReadFromJsonAsync<TransactionDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.True(updated.IsTransfer);
        Assert.Equal(transferId, updated.CategoryId);
    }

    [Fact]
    public async Task PatchFromTransferToUserCategory_ClearsIsTransfer()
    {
        var (client, txId, transferId, userCategoryId) = await SetupWithTransaction();

        // First: mark as transfer.
        (await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(transferId, null))).EnsureSuccessStatusCode();

        // Then: switch to user category.
        var resp = await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(userCategoryId, null));
        resp.EnsureSuccessStatusCode();

        var updated = (await resp.Content.ReadFromJsonAsync<TransactionDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.False(updated.IsTransfer);
        Assert.Equal(userCategoryId, updated.CategoryId);
    }

    [Fact]
    public async Task PatchToNullCategory_ClearsIsTransfer()
    {
        var (client, txId, transferId, _) = await SetupWithTransaction();

        (await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(transferId, null))).EnsureSuccessStatusCode();

        var resp = await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(null, null));
        resp.EnsureSuccessStatusCode();

        var updated = (await resp.Content.ReadFromJsonAsync<TransactionDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.False(updated.IsTransfer);
        Assert.Null(updated.CategoryId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> NewFamilyClient()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;
        return _factory.CreateClientWithFamily(family.Id);
    }

    private static async Task<CategoryDto> GetTransferCategory(HttpClient client)
    {
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", IntegrationTestFactory.JsonOpts))!;
        return categories.Single(c => c.Kind == CategoryKind.Transfer);
    }

    private async Task<(HttpClient client, Guid txId, Guid transferId, Guid userCategoryId)> SetupWithTransaction()
    {
        var client = await NewFamilyClient();
        var transfer = await GetTransferCategory(client);

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Shopping"));
        var userCategory = (await catResp.Content.ReadFromJsonAsync<CategoryDto>(IntegrationTestFactory.JsonOpts))!;

        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;
        var acctResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase", "Chase", AccountType.CreditCard, "Chase"));
        var account = (await acctResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        await using var stream = File.OpenRead(SamplePath);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(account.Id.ToString()), "accountId");
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "ChaseSample.csv");

        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();

        var ledger = (await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions", IntegrationTestFactory.JsonOpts))!;
        return (client, ledger.Items.First().Id, transfer.Id, userCategory.Id);
    }

    private record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);
}
