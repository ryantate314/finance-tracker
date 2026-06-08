using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class DiscoverParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "DiscoverSample.csv"));

    [Fact]
    public void BankCode_IsDiscover()
    {
        var parser = new DiscoverParser();
        Assert.Equal("Discover", parser.BankCode);
    }

    [Fact]
    public void Parse_ReadsEveryMoneyRow()
    {
        var parser = new DiscoverParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public void Parse_Purchase_HasNegativeAmount()
    {
        var parser = new DiscoverParser();
        using var stream = OpenSample();

        // Discover reports purchases as positive; canonical ledger stores outflows as negative.
        var purchase = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), purchase.Date);
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), purchase.PostedDate);
        Assert.Equal(-33.14m, purchase.Amount);
        Assert.Equal("SAMPLE RESTAURANT #00000 000-0000000 ST", purchase.Description);
        Assert.Null(purchase.Merchant);
    }

    [Fact]
    public void Parse_Payment_HasPositiveAmount()
    {
        var parser = new DiscoverParser();
        using var stream = OpenSample();

        // Discover reports payments/credits as negative; canonical ledger stores inflows as positive.
        var payment = parser.Parse(stream)
            .First(r => r.Description == "INTERNET PAYMENT - THANK YOU");

        Assert.Equal(266.65m, payment.Amount);
    }

    [Fact]
    public void Parse_PostDateDiffersFromTransDate_KeepsBoth()
    {
        var parser = new DiscoverParser();
        using var stream = OpenSample();

        var row = parser.Parse(stream)
            .First(r => r.Description.StartsWith("SAMPLE MARKETPLACE PMTS"));

        Assert.Equal(new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc), row.Date);
        Assert.Equal(new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc), row.PostedDate);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsImportException400()
    {
        var parser = new DiscoverParser();
        var csv = "\"Trans. Date\",\"Description\",\"Amount\"\n" +
                  "\"05/04/2026\",\"Purchase\",\"10.00\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Missing column", ex.Title);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new DiscoverParser();
        var csv = "\"Trans. Date\",\"Post Date\",\"Description\",\"Amount\"\n" +
                  "\"05/04/2026\",\"05/04/2026\",\"Purchase\",\"not-a-number\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Amount", ex.Title);
    }

    [Fact]
    public void Parse_MalformedDate_ThrowsImportException400()
    {
        var parser = new DiscoverParser();
        var csv = "\"Trans. Date\",\"Post Date\",\"Description\",\"Amount\"\n" +
                  "\"not-a-date\",\"05/04/2026\",\"Purchase\",\"10.00\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Trans. Date", ex.Title);
    }
}
