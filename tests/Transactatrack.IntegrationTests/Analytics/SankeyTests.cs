using System.Net.Http.Json;
using Transactatrack.Application.Analytics;
using Transactatrack.Domain.Enums;
using static Transactatrack.IntegrationTests.Transfers.TransferTestHarness;

namespace Transactatrack.IntegrationTests.Analytics;

public class SankeyTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public SankeyTests(IntegrationTestFactory factory) => _factory = factory;

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

    private static async Task<SankeyDto> GetSankey(HttpClient client, params Guid[] accountIds)
    {
        var url = "/api/analytics/sankey?from=2026-01-01&to=2026-01-31";
        if (accountIds.Length > 0) url += "&accountIds=" + string.Join(",", accountIds);
        return (await client.GetFromJsonAsync<SankeyDto>(url, IntegrationTestFactory.JsonOpts))!;
    }

    [Fact]
    public async Task Unfiltered_HasAllThreeLayers_WithCorrectValues()
    {
        var (client, personal, family) = await Setup();

        var sankey = await GetSankey(client);

        // income -> account (the paycheck)
        Assert.Contains(sankey.Links, l => l.Target == $"account:{personal}" && l.Source.StartsWith("income:") && l.Value == 3000m);
        // account -> account (the internal personal->family contribution, drawn even though it nets in cashflow)
        Assert.Contains(sankey.Links, l => l.Source == $"account:{personal}" && l.Target == $"account:{family}" && l.Value == 1000m);
        // account -> category (the family expense lands in Uncategorized)
        Assert.Contains(sankey.Links, l => l.Source == $"account:{family}" && l.Target == "category:uncategorized" && l.Value == 300m);
    }

    [Fact]
    public async Task NoZeroOrNegativeLinks()
    {
        var (client, _, _) = await Setup();

        var sankey = await GetSankey(client);

        Assert.NotEmpty(sankey.Links);
        Assert.All(sankey.Links, l => Assert.True(l.Value > 0m));
    }

    [Fact]
    public async Task CreditCardPositives_AreTransfersIn_NotIncome()
    {
        // Mirrors Ryan's real data: card payments (tagged transfer, no imported counterpart) and a
        // refund are positive amounts on a credit card. Neither is earned income — both must come
        // from the "Transfers in" source so the card never appears as an income-fed node.
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var card = await CreateAccount(client, owner, "Chase Card", Transactatrack.Domain.Enums.AccountType.CreditCard);

        await ImportAndCommit(client, card, ChaseCsv(
            new Row("01/10/2026", "STORE PURCHASE", -100m),
            new Row("01/12/2026", "PAYMENT THANK YOU", 100m),
            new Row("01/15/2026", "REFUND", 30m)));

        // Tag the payment as a transfer (no cross-account counterpart exists to auto-match).
        var payment = (await Ledger(client, card)).Single(t => t.Amount == 100m);
        await Categorize(client, payment.Id, await SystemCategoryId(client, CategoryKind.Transfer));

        var sankey = await GetSankey(client, card);

        Assert.DoesNotContain(sankey.Links, l => l.Source.StartsWith("income:"));
        Assert.Contains(sankey.Links, l => l.Source == "transfersin" && l.Target == $"account:{card}" && l.Value == 130m);
        Assert.Contains(sankey.Links, l => l.Source == $"account:{card}" && l.Target == "category:uncategorized" && l.Value == 100m);
    }

    [Fact]
    public async Task EveryLinkEndpoint_HasANode()
    {
        var (client, _, _) = await Setup();

        var sankey = await GetSankey(client);

        var nodeIds = sankey.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var link in sankey.Links)
        {
            Assert.Contains(link.Source, nodeIds);
            Assert.Contains(link.Target, nodeIds);
        }
    }
}
