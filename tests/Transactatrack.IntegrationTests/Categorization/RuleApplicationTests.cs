using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.CategoryRules;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Categorization;

public class RuleApplicationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv");

    public RuleApplicationTests(IntegrationTestFactory factory) => _factory = factory;

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

        return (client, account.Id, category.Id);
    }

    private async Task SeedRule(HttpClient client, Guid categoryId, string pattern)
    {
        var req = new CreateCategoryRuleRequest(10, RuleMatchField.Description, RuleMatchType.Contains, pattern,
            null, null, categoryId, null, RuleScope.Family, null, true);
        (await client.PostAsJsonAsync("/api/category-rules", req)).EnsureSuccessStatusCode();
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
    public async Task Upload_WithMatchingRule_RowsPreCategorized()
    {
        var (client, accountId, categoryId) = await SetupAsync();
        // The sample CSV contains "FIXTURE PAYMENT" and other descriptions.
        // Seed a rule that matches the known fixture payment row.
        await SeedRule(client, categoryId, "FIXTURE PAYMENT");

        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream);
        var resp = await client.PostAsync("/api/imports", form);
        resp.EnsureSuccessStatusCode();

        var preview = (await resp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        var matchedRow = preview.Sample.FirstOrDefault(r => !r.IsDuplicate && r.CategoryId is not null);
        Assert.NotNull(matchedRow);
        Assert.Equal(categoryId, matchedRow.CategoryId);
        Assert.Equal(CategorizationSource.Rule, matchedRow.CategorizationSource);
    }

    [Fact]
    public async Task Upload_RuleInFamilyA_DoesNotCategorizeFamilyBImport()
    {
        var (clientA, accountAId, categoryIdA) = await SetupAsync();
        var (clientB, accountBId, _) = await SetupAsync();

        // Seed a rule in Family A
        await SeedRule(clientA, categoryIdA, "FIXTURE PAYMENT");

        // Upload in Family B — rows should be uncategorized
        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountBId, stream);
        var resp = await clientB.PostAsync("/api/imports", form);
        resp.EnsureSuccessStatusCode();

        var preview = (await resp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.All(preview.Sample.Where(r => !r.IsDuplicate), r => Assert.Null(r.CategoryId));
    }

    [Fact]
    public async Task Patch_PendingTransaction_SetsCategorySourceManual()
    {
        var (client, accountId, categoryId) = await SetupAsync();
        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream);
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;

        // Load the full batch detail to get transaction id from the DB
        var batchId = preview.BatchId;
        var detail = (await client.GetFromJsonAsync<ImportBatchDetailDto>(
            $"/api/imports/{batchId}", IntegrationTestFactory.JsonOpts))!;

        // We need transaction IDs; use the ledger after commit... but for Pending we need them via detail.
        // Actually detail returns rows without IDs. We'll use the ledger after commit via a different path.
        // Let's commit, then patch via ledger.
        (await client.PostAsync($"/api/imports/{batchId}/commit", null)).EnsureSuccessStatusCode();

        var ledger = (await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions", IntegrationTestFactory.JsonOpts))!;
        var tx = ledger.Items.First();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/transactions/{tx.Id}",
            new UpdateTransactionCategoryRequest(categoryId, null));
        patchResp.EnsureSuccessStatusCode();

        var updated = (await patchResp.Content.ReadFromJsonAsync<TransactionDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.Equal(categoryId, updated.CategoryId);
        Assert.Equal(CategorizationSource.Manual, updated.CategorizationSource);
        Assert.False(updated.NeedsReview);
        Assert.Null(updated.AppliedRuleId);
    }

    [Fact]
    public async Task RerunRules_OverwritesRuleSource_PreservesManual()
    {
        var (client, accountId, categoryId) = await SetupAsync();
        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream);
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        var batchId = preview.BatchId;

        // Add a rule and re-run
        await SeedRule(client, categoryId, "FIXTURE PAYMENT");
        var rerunResp = await client.PostAsync($"/api/imports/{batchId}/rerun-rules", null);
        rerunResp.EnsureSuccessStatusCode();

        // Reload detail to verify
        var detail = (await client.GetFromJsonAsync<ImportBatchDetailDto>(
            $"/api/imports/{batchId}", IntegrationTestFactory.JsonOpts))!;
        var matched = detail.Transactions.FirstOrDefault(r => r.CategoryId is not null);
        Assert.NotNull(matched);
        Assert.Equal(CategorizationSource.Rule, matched.CategorizationSource);
    }

    private record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);
    private record ImportBatchDetailDto(ImportBatchDto Batch, List<ImportPreviewRowDto> Transactions);
}
