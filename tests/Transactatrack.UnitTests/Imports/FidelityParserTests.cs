using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class FidelityParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "FidelitySample.csv"));

    [Fact]
    public void BankCode_IsFidelity()
    {
        var parser = new FidelityParser();
        Assert.Equal("Fidelity", parser.BankCode);
    }

    [Fact]
    public void Parse_ReturnsThreeRows()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Parse_SkipsReinvestmentRows()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.DoesNotContain(rows, r => r.Description.StartsWith("REINVESTMENT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_DebitCardPurchase_HasNegativeAmountAndStrippedDescription()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        var purchase = parser.Parse(stream).First(r => r.Description.StartsWith("DEBIT CARD PURCHASE"));

        Assert.Equal(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), purchase.Date);
        Assert.Equal(-42.50m, purchase.Amount);
        Assert.Equal("DEBIT CARD PURCHASE SAMPLE MERCHANT ANYTOWN ST 061426 AUTHID:000001", purchase.Description);
        Assert.Null(purchase.PostedDate);
        Assert.Null(purchase.Merchant);
    }

    [Fact]
    public void Parse_DividendReceived_HasPositiveAmount()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        var dividend = parser.Parse(stream).First(r => r.Description.StartsWith("DIVIDEND RECEIVED"));

        Assert.Equal(new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), dividend.Date);
        Assert.Equal(1.00m, dividend.Amount);
        Assert.Equal("DIVIDEND RECEIVED FIDELITY GOVERNMENT CASH RESERVES (FDRXX)", dividend.Description);
    }

    [Fact]
    public void Parse_HsaContribution_HasPositiveAmount()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        var contribution = parser.Parse(stream).First(r => r.Description.StartsWith("PARTIC CONTR"));

        Assert.Equal(new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc), contribution.Date);
        Assert.Equal(500.00m, contribution.Amount);
    }

    [Fact]
    public void Parse_FooterRows_AreSkipped()
    {
        var parser = new FidelityParser();
        using var stream = OpenSample();

        // footer rows have unparseable "Run Date" values — should not throw
        var ex = Record.Exception(() => parser.Parse(stream).ToList());

        Assert.Null(ex);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsImportException400()
    {
        var parser = new FidelityParser();
        var csv = "Run Date,Action,Symbol\n06-30-2026,DIVIDEND RECEIVED SOMETHING (Cash),FDRXX\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Missing column", ex.Title);
        Assert.Contains("Amount ($)", ex.Title);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new FidelityParser();
        var csv = "Run Date,Action,Symbol,Description,Type,Price ($),Quantity,Commission ($),Fees ($),Accrued Interest ($),Amount ($),Cash Balance ($),Settlement Date\n" +
                  "06-15-2026,DEBIT CARD PURCHASE SAMPLE MERCHANT (Cash),,No Description,Cash,,0,,,,not-a-number,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Amount ($)", ex.Title);
    }
}
