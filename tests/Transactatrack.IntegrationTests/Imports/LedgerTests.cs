using System.Net.Http.Headers;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Imports;

public class LedgerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv");

    public LedgerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(HttpClient client, Guid accountId)> SetupCommittedAsync()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;

        var client = _factory.CreateClientWithFamily(family.Id);
        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;

        var accountResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase Card", "Chase", AccountType.CreditCard, "Chase"));
        var account = (await accountResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        await using var stream = File.OpenRead(SamplePath);
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(account.Id.ToString()), "accountId");
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "ChaseSample.csv");

        var preview = (await (await client.PostAsync("/api/imports", form)).Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();

        return (client, account.Id);
    }

    [Fact]
    public async Task Ledger_DefaultPaging_Returns50Items()
    {
        var (client, _) = await SetupCommittedAsync();

        var resp = await client.GetAsync("/api/transactions");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;

        Assert.Equal(176, page.TotalCount);
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Ledger_RespectsCustomPageSize()
    {
        var (client, _) = await SetupCommittedAsync();

        var resp = await client.GetAsync("/api/transactions?pageSize=10&page=2");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(2, page.Page);
    }

    [Fact]
    public async Task Ledger_SearchByDescription_IsCaseInsensitive()
    {
        var (client, _) = await SetupCommittedAsync();

        var resp = await client.GetAsync("/api/transactions?q=fixture&pageSize=200");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, t =>
            Assert.Contains("fixture", t.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ledger_FilterByAccount()
    {
        var (client, accountId) = await SetupCommittedAsync();

        var resp = await client.GetAsync($"/api/transactions?accountIds={accountId}&pageSize=200");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;

        Assert.Equal(176, page.TotalCount);
        Assert.All(page.Items, t => Assert.Equal(accountId, t.AccountId));
    }

    [Fact]
    public async Task Ledger_FilterByDateRange()
    {
        var (client, _) = await SetupCommittedAsync();

        var resp = await client.GetAsync("/api/transactions?from=2026-05-01&to=2026-05-04&pageSize=200");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(IntegrationTestFactory.JsonOpts))!;

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, t =>
        {
            Assert.True(t.Date >= new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(t.Date <= new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));
        });
    }
}
