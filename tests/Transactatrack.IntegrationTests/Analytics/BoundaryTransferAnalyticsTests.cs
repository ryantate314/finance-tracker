using System.Net.Http.Json;
using Transactatrack.Application.Analytics;
using Transactatrack.Domain.Enums;
using static Transactatrack.IntegrationTests.Transfers.TransferTestHarness;

namespace Transactatrack.IntegrationTests.Analytics;

public class BoundaryTransferAnalyticsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public BoundaryTransferAnalyticsTests(IntegrationTestFactory factory) => _factory = factory;

    // Personal: +3000 income, -200 groceries, -1000 contribution to the family account.
    // Family:   +1000 (auto-matched with the -1000), -300 family expense.
    private async Task<(HttpClient client, Guid personal, Guid family)> Setup()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var personal = await CreateAccount(client, owner, "Personal Checking");
        var family = await CreateAccount(client, owner, "Family Account");
        var groceries = await CreateCategory(client, "Groceries");

        await ImportAndCommit(client, personal, ChaseCsv(
            new Row("01/05/2026", "PAYCHECK", 3000m),
            new Row("01/05/2026", "SUPERMARKET", -200m),
            new Row("01/05/2026", "TO FAMILY", -1000m)));
        await ImportAndCommit(client, family, ChaseCsv(
            new Row("01/06/2026", "FROM RYAN", 1000m),
            new Row("01/07/2026", "FAMILY UTILITIES", -300m)));

        var personalLedger = await Ledger(client, personal);
        await Categorize(client, personalLedger.Single(t => t.Amount == -200m).Id, groceries);
        await Categorize(client, personalLedger.Single(t => t.Amount == 3000m).Id, await SystemCategoryId(client, CategoryKind.Income));

        return (client, personal, family);
    }

    private static async Task<MonthlyCashflowItemDto> Cashflow(HttpClient client, params Guid[] accountIds)
    {
        var url = "/api/analytics/monthly-cashflow?from=2026-01-01&to=2026-01-31";
        if (accountIds.Length > 0) url += "&accountIds=" + string.Join(",", accountIds);
        var items = (await client.GetFromJsonAsync<List<MonthlyCashflowItemDto>>(url, IntegrationTestFactory.JsonOpts))!;
        return items.Single();
    }

    private static async Task<List<CategoryBreakdownItemDto>> Breakdown(HttpClient client, params Guid[] accountIds)
    {
        var url = "/api/analytics/category-breakdown?from=2026-01-01&to=2026-01-31";
        if (accountIds.Length > 0) url += "&accountIds=" + string.Join(",", accountIds);
        return (await client.GetFromJsonAsync<List<CategoryBreakdownItemDto>>(url, IntegrationTestFactory.JsonOpts))!;
    }

    [Fact]
    public async Task AllAccounts_InternalTransferNetsToZero()
    {
        var (client, _, _) = await Setup();

        var cf = await Cashflow(client); // no account filter => whole family

        Assert.Equal(3000m, cf.Income);
        Assert.Equal(-500m, cf.Expense);   // -200 groceries + -300 family
        Assert.Equal(0m, cf.TransfersIn);
        Assert.Equal(0m, cf.TransfersOut);
        Assert.Equal(2500m, cf.Net);
    }

    [Fact]
    public async Task ScopedToFamilyAccount_ContributionIsIncome()
    {
        var (client, _, family) = await Setup();

        var cf = await Cashflow(client, family);

        Assert.Equal(0m, cf.Income);
        Assert.Equal(-300m, cf.Expense);
        Assert.Equal(1000m, cf.TransfersIn);   // the contribution arrives from outside the scope
        Assert.Equal(0m, cf.TransfersOut);
        Assert.Equal(700m, cf.Net);
    }

    [Fact]
    public async Task ScopedToPersonalAccount_ContributionIsTransfersOut()
    {
        var (client, personal, _) = await Setup();

        var cf = await Cashflow(client, personal);

        Assert.Equal(3000m, cf.Income);
        Assert.Equal(-200m, cf.Expense);
        Assert.Equal(0m, cf.TransfersIn);
        Assert.Equal(-1000m, cf.TransfersOut);
        Assert.Equal(1800m, cf.Net);
    }

    [Fact]
    public async Task Breakdown_ScopedPersonal_HasSyntheticTransfersOut_NotTransferCategory()
    {
        var (client, personal, _) = await Setup();

        var breakdown = await Breakdown(client, personal);

        var groceries = breakdown.Single(b => b.CategoryName == "Groceries");
        Assert.Equal(200m, groceries.Amount);

        var transfersOut = breakdown.Single(b => b.IsTransfersBucket);
        Assert.Equal(1000m, transfersOut.Amount);
        Assert.Equal("Transfers out", transfersOut.CategoryName);

        // No real "Transfer" category should ever appear in the spending breakdown.
        Assert.DoesNotContain(breakdown, b => b.CategoryName == "Transfer");
    }

    [Fact]
    public async Task Breakdown_AllAccounts_NoTransfersBucket()
    {
        var (client, _, _) = await Setup();

        var breakdown = await Breakdown(client);

        Assert.DoesNotContain(breakdown, b => b.IsTransfersBucket);
        Assert.Equal(200m, breakdown.Single(b => b.CategoryName == "Groceries").Amount);
        Assert.Equal(300m, breakdown.Single(b => b.CategoryName == "Uncategorized").Amount);
    }
}
