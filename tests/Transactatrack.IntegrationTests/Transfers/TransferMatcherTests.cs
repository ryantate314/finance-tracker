using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;
using static Transactatrack.IntegrationTests.Transfers.TransferTestHarness;

namespace Transactatrack.IntegrationTests.Transfers;

public class TransferMatcherTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public TransferMatcherTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task AutoMatch_OnCommit_PairsEqualAndOpposite()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var checking = await CreateAccount(client, owner, "Checking");
        var card = await CreateAccount(client, owner, "Card");
        var transferCatId = await GetTransferCategoryId(client);

        await ImportAndCommit(client, checking, ChaseCsv(new Row("01/10/2026", "PAYMENT TO CARD", -500m)));
        await ImportAndCommit(client, card, ChaseCsv(new Row("01/11/2026", "AUTOPAY RECEIVED", 500m)));

        var ledger = await Ledger(client, checking, card);
        var outflow = ledger.Single(t => t.Amount == -500m);
        var inflow = ledger.Single(t => t.Amount == 500m);

        Assert.True(outflow.IsTransfer);
        Assert.True(inflow.IsTransfer);
        Assert.NotNull(outflow.TransferGroupId);
        Assert.Equal(outflow.TransferGroupId, inflow.TransferGroupId);
        Assert.Equal(transferCatId, outflow.CategoryId);
        Assert.Equal(transferCatId, inflow.CategoryId);
    }

    [Fact]
    public async Task OutsideWindow_NotPaired()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var a = await CreateAccount(client, owner, "A");
        var b = await CreateAccount(client, owner, "B");

        await ImportAndCommit(client, a, ChaseCsv(new Row("01/01/2026", "OUT", -500m)));
        await ImportAndCommit(client, b, ChaseCsv(new Row("01/10/2026", "IN", 500m))); // 9 days apart

        var ledger = await Ledger(client, a, b);
        Assert.All(ledger, t => Assert.False(t.IsTransfer));
        Assert.All(ledger, t => Assert.Null(t.TransferGroupId));
    }

    [Fact]
    public async Task SameAccount_NotPaired()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var a = await CreateAccount(client, owner, "A");

        await ImportAndCommit(client, a, ChaseCsv(
            new Row("01/10/2026", "OUT", -500m),
            new Row("01/10/2026", "IN", 500m)));

        var ledger = await Ledger(client, a);
        Assert.All(ledger, t => Assert.False(t.IsTransfer));
    }

    [Fact]
    public async Task Rescan_IsIdempotent()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var a = await CreateAccount(client, owner, "A");
        var b = await CreateAccount(client, owner, "B");

        await ImportAndCommit(client, a, ChaseCsv(new Row("01/10/2026", "OUT", -500m)));
        await ImportAndCommit(client, b, ChaseCsv(new Row("01/10/2026", "IN", 500m)));

        var groupBefore = (await Ledger(client, a, b)).Single(t => t.Amount == -500m).TransferGroupId;

        var rescanResp = await client.PostAsync("/api/transfers/rescan", null);
        rescanResp.EnsureSuccessStatusCode();
        var result = (await rescanResp.Content.ReadFromJsonAsync<TransferMatchResultDto>(IntegrationTestFactory.JsonOpts))!;

        Assert.Equal(0, result.Paired); // already paired; nothing new
        var groupAfter = (await Ledger(client, a, b)).Single(t => t.Amount == -500m).TransferGroupId;
        Assert.Equal(groupBefore, groupAfter);
    }

    [Fact]
    public async Task ManualLink_ThenUnlink_RoundTrips()
    {
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");
        var a = await CreateAccount(client, owner, "A");
        var b = await CreateAccount(client, owner, "B");

        // Non-opposite amounts: the auto-matcher leaves them alone, so this exercises the manual escape hatch.
        await ImportAndCommit(client, a, ChaseCsv(new Row("01/10/2026", "OUT", -300m)));
        await ImportAndCommit(client, b, ChaseCsv(new Row("01/10/2026", "OUT2", -700m)));

        var before = await Ledger(client, a, b);
        var txA = before.Single(t => t.Amount == -300m);
        var txB = before.Single(t => t.Amount == -700m);
        Assert.False(txA.IsTransfer);

        var linkResp = await client.PostAsJsonAsync("/api/transfers/link",
            new { transactionIdA = txA.Id, transactionIdB = txB.Id });
        linkResp.EnsureSuccessStatusCode();

        var linked = await Ledger(client, a, b);
        var lA = linked.Single(t => t.Id == txA.Id);
        var lB = linked.Single(t => t.Id == txB.Id);
        Assert.True(lA.IsTransfer);
        Assert.True(lB.IsTransfer);
        Assert.NotNull(lA.TransferGroupId);
        Assert.Equal(lA.TransferGroupId, lB.TransferGroupId);

        var unlinkResp = await client.PostAsync($"/api/transfers/{lA.TransferGroupId}/unlink", null);
        unlinkResp.EnsureSuccessStatusCode();

        var unlinked = await Ledger(client, a, b);
        Assert.All(unlinked, t => Assert.False(t.IsTransfer));
        Assert.All(unlinked, t => Assert.Null(t.TransferGroupId));
    }

    [Fact]
    public async Task DiscoverPayment_ImportsAsPositive()
    {
        // Guards the matcher's equal-and-opposite assumption: a CC "payment received" must land
        // positive (an inflow to the liability) so it can pair with a checking debit.
        var client = await NewFamilyClient(_factory);
        var owner = await CreateOwner(client, "Ryan");

        var resp = await client.PostAsJsonAsync("/api/accounts",
            new Transactatrack.Application.Accounts.CreateAccountRequest(owner, "Discover", "Discover", AccountType.CreditCard, "Discover"));
        resp.EnsureSuccessStatusCode();
        var discover = (await resp.Content.ReadFromJsonAsync<Transactatrack.Application.Accounts.AccountDto>(IntegrationTestFactory.JsonOpts))!.Id;

        // Discover exports payments as a negative Amount; the parser flips it to canonical (+).
        var csv = "Trans. Date,Post Date,Description,Amount\n01/10/2026,01/10/2026,DIRECTPAY FULL BALANCE,-500.00\n";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(discover.ToString()), "accountId");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "discover.csv");
        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<Transactatrack.Application.Imports.ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();

        var ledger = await Ledger(client, discover);
        Assert.Equal(500m, ledger.Single().Amount);
    }

    private static async Task<Guid> GetTransferCategoryId(HttpClient client)
    {
        var cats = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", IntegrationTestFactory.JsonOpts))!;
        return cats.Single(c => c.Kind == CategoryKind.Transfer).Id;
    }

    private record TransferMatchResultDto(int Paired, int Scanned);
}
