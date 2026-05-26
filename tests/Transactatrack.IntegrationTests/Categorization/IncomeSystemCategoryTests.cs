using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Analytics;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Categorization;

public class IncomeSystemCategoryTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public IncomeSystemCategoryTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task DeletingSystemIncomeCategory_Returns409()
    {
        var client = await NewFamilyClient();
        var income = await GetIncomeCategory(client);

        var resp = await client.DeleteAsync($"/api/categories/{income.Id}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task RenamingSystemIncomeCategory_Returns409()
    {
        var client = await NewFamilyClient();
        var income = await GetIncomeCategory(client);

        var resp = await client.PutAsJsonAsync($"/api/categories/{income.Id}", new UpdateCategoryRequest("Renamed"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task MonthlyCashflow_BucketsByCategoryKind()
    {
        var client = await NewFamilyClient();
        var income = await GetIncomeCategory(client);

        var foodResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Food"));
        var food = (await foodResp.Content.ReadFromJsonAsync<CategoryDto>(IntegrationTestFactory.JsonOpts))!;

        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;
        var accountResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase", "Chase", AccountType.CreditCard, "Chase"));
        var account = (await accountResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        // Custom CSV — all rows in July 2026 — covering the bucket-mixing cases.
        // Income bucket   = +1000 + +500 + -50 + +100 (uncat positive) = 1550
        // Expense bucket  = -200 + +30 + -75 (uncat negative)          = -245
        var csv = new StringBuilder();
        csv.AppendLine("Transaction Date,Post Date,Description,Category,Type,Amount,Memo");
        csv.AppendLine("07/01/2026,07/01/2026,INC PAYCHECK A,,Payment,1000.00,");
        csv.AppendLine("07/05/2026,07/05/2026,INC PAYCHECK B,,Payment,500.00,");
        csv.AppendLine("07/10/2026,07/10/2026,INC PAYCHECK REFUND,,Sale,-50.00,");
        csv.AppendLine("07/12/2026,07/12/2026,EXP GROCERY,,Sale,-200.00,");
        csv.AppendLine("07/13/2026,07/13/2026,EXP GROCERY REFUND,,Sale,30.00,");
        csv.AppendLine("07/14/2026,07/14/2026,UNCAT POSITIVE,,Payment,100.00,");
        csv.AppendLine("07/15/2026,07/15/2026,UNCAT NEGATIVE,,Sale,-75.00,");

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(account.Id.ToString()), "accountId");
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "cashflow.csv");
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();

        var ledger = (await client.GetFromJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions?pageSize=200", IntegrationTestFactory.JsonOpts))!;
        var byDesc = ledger.Items.ToDictionary(t => t.Description, t => t.Id);

        await Patch(client, byDesc["INC PAYCHECK A"], income.Id);
        await Patch(client, byDesc["INC PAYCHECK B"], income.Id);
        await Patch(client, byDesc["INC PAYCHECK REFUND"], income.Id);
        await Patch(client, byDesc["EXP GROCERY"], food.Id);
        await Patch(client, byDesc["EXP GROCERY REFUND"], food.Id);
        // UNCAT POSITIVE / NEGATIVE intentionally left uncategorized.

        var cashflow = (await client.GetFromJsonAsync<List<MonthlyCashflowItemDto>>(
            "/api/analytics/monthly-cashflow?from=2026-07-01&to=2026-07-31",
            IntegrationTestFactory.JsonOpts))!;

        Assert.Single(cashflow);
        var july = cashflow[0];
        Assert.Equal(2026, july.Year);
        Assert.Equal(7, july.Month);
        Assert.Equal(1550m, july.Income);
        Assert.Equal(-245m, july.Expense);
        Assert.Equal(1305m, july.Net);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> NewFamilyClient()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;
        return _factory.CreateClientWithFamily(family.Id);
    }

    private static async Task<CategoryDto> GetIncomeCategory(HttpClient client)
    {
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", IntegrationTestFactory.JsonOpts))!;
        return categories.Single(c => c.Kind == CategoryKind.Income);
    }

    private static async Task Patch(HttpClient client, Guid txId, Guid categoryId)
    {
        var resp = await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(categoryId, null));
        resp.EnsureSuccessStatusCode();
    }

    private record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);
}
