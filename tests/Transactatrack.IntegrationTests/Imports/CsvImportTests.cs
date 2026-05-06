using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Imports;

public class CsvImportTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv");

    public CsvImportTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(HttpClient client, Guid familyId, Guid accountId)> SetupAccountAsync(string? bankCode = "Chase")
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;

        var client = _factory.CreateClientWithFamily(family.Id);
        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Test Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;

        var accountResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase Card", "Chase", AccountType.CreditCard, bankCode));
        var account = (await accountResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        return (client, family.Id, account.Id);
    }

    private static MultipartFormDataContent BuildUpload(Guid accountId, Stream csv, string filename)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(accountId.ToString()), "accountId");
        var fileContent = new StreamContent(csv);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", filename);
        return form;
    }

    [Fact]
    public async Task Upload_SampleCsv_KeepsAllRowsIncludingIntraFileCollisions()
    {
        var (client, _, accountId) = await SetupAccountAsync();
        await using var stream = File.OpenRead(SamplePath);

        using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
        var resp = await client.PostAsync("/api/imports", form);
        resp.EnsureSuccessStatusCode();

        var preview = (await resp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        // Synthetic fixture has 176 rows; intra-file collisions (FIXTURE DUP A/B pairs) are
        // treated as distinct transactions, not duplicates.
        Assert.Equal(176, preview.TotalRows);
        Assert.Equal(176, preview.NewCount);
        Assert.Equal(0, preview.DuplicateCount);
        Assert.NotEmpty(preview.Sample);
    }

    [Fact]
    public async Task Upload_WhilePendingExists_Returns409()
    {
        var (client, _, accountId) = await SetupAccountAsync();

        await using (var first = File.OpenRead(SamplePath))
        {
            using var form = BuildUpload(accountId, first, "ChaseSample.csv");
            (await client.PostAsync("/api/imports", form)).EnsureSuccessStatusCode();
        }

        await using var second = File.OpenRead(SamplePath);
        using var form2 = BuildUpload(accountId, second, "ChaseSample.csv");
        var resp = await client.PostAsync("/api/imports", form2);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_AccountWithoutBankCode_Returns400()
    {
        var (client, _, accountId) = await SetupAccountAsync(bankCode: null);
        await using var stream = File.OpenRead(SamplePath);

        using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
        var resp = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownBankCode_Returns400()
    {
        var (client, _, accountId) = await SetupAccountAsync(bankCode: "MysteryBank");
        await using var stream = File.OpenRead(SamplePath);

        using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
        var resp = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Discard_RemovesBatchAndTransactions()
    {
        var (client, _, accountId) = await SetupAccountAsync();
        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
        var preview = (await (await client.PostAsync("/api/imports", form)).Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;

        var discardResp = await client.PostAsync($"/api/imports/{preview.BatchId}/discard", null);
        discardResp.EnsureSuccessStatusCode();

        var listResp = await client.GetAsync("/api/imports");
        var batches = (await listResp.Content.ReadFromJsonAsync<List<ImportBatchDto>>(IntegrationTestFactory.JsonOpts))!;
        Assert.DoesNotContain(batches, b => b.Id == preview.BatchId);
    }

    [Fact]
    public async Task Commit_FlipsStatus_TransactionsVisibleInLedger()
    {
        var (client, _, accountId) = await SetupAccountAsync();
        await using (var stream = File.OpenRead(SamplePath))
        {
            using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
            var preview = (await (await client.PostAsync("/api/imports", form)).Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
            (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();
        }

        var ledgerResp = await client.GetAsync("/api/transactions?pageSize=200");
        ledgerResp.EnsureSuccessStatusCode();
        var page = (await ledgerResp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;
        Assert.Equal(176, page.TotalCount);
    }

    [Fact]
    public async Task ReUpload_AfterCommit_AllRowsAreDuplicates()
    {
        var (client, _, accountId) = await SetupAccountAsync();
        await using (var stream = File.OpenRead(SamplePath))
        {
            using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
            var preview = (await (await client.PostAsync("/api/imports", form)).Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
            (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();
        }

        await using var stream2 = File.OpenRead(SamplePath);
        using var form2 = BuildUpload(accountId, stream2, "ChaseSample.csv");
        var resp = await client.PostAsync("/api/imports", form2);
        resp.EnsureSuccessStatusCode();
        var preview2 = (await resp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;

        Assert.Equal(176, preview2.TotalRows);
        Assert.Equal(0, preview2.NewCount);
        Assert.Equal(176, preview2.DuplicateCount);  // all rows match against committed batch
    }

    [Fact]
    public async Task Imports_AreScopedToFamily()
    {
        var (clientA, _, accountIdA) = await SetupAccountAsync();
        await using (var stream = File.OpenRead(SamplePath))
        {
            using var form = BuildUpload(accountIdA, stream, "ChaseSample.csv");
            (await clientA.PostAsync("/api/imports", form)).EnsureSuccessStatusCode();
        }

        var (clientB, _, _) = await SetupAccountAsync();
        var listResp = await clientB.GetAsync("/api/imports");
        listResp.EnsureSuccessStatusCode();
        var batchesB = (await listResp.Content.ReadFromJsonAsync<List<ImportBatchDto>>(IntegrationTestFactory.JsonOpts))!;
        Assert.Empty(batchesB);
    }

    [Fact]
    public async Task PendingBatch_TransactionsExcludedFromLedger()
    {
        var (client, _, accountId) = await SetupAccountAsync();
        await using var stream = File.OpenRead(SamplePath);
        using var form = BuildUpload(accountId, stream, "ChaseSample.csv");
        (await client.PostAsync("/api/imports", form)).EnsureSuccessStatusCode();

        var ledgerResp = await client.GetAsync("/api/transactions");
        ledgerResp.EnsureSuccessStatusCode();
        var page = (await ledgerResp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;
        Assert.Equal(0, page.TotalCount);
    }
}
