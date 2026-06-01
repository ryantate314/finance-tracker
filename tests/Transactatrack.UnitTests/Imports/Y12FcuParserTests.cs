using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class Y12FcuParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Y12FcuSample.csv"));

    [Fact]
    public void BankCode_IsY12Fcu()
    {
        var parser = new Y12FcuParser();
        Assert.Equal("Y12FCU", parser.BankCode);
    }

    [Fact]
    public void Parse_SkipsMetadataPreambleAndAmountlessRows()
    {
        var parser = new Y12FcuParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        // 3 metadata lines + header skipped; the $0.00 "COMMENT" row (no debit/credit) dropped.
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Parse_PurchaseRow_UsesMemoAsDescriptionAndNegativeAmount()
    {
        var parser = new Y12FcuParser();
        using var stream = OpenSample();

        var purchase = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc), purchase.Date);
        Assert.Null(purchase.PostedDate);
        Assert.Equal(-40.30m, purchase.Amount);
        Assert.Equal("SAMPLE GROCERY #000 ANYTOWN ST Date 05/29/26 5411-Misc. Retail/ Card #0000", purchase.Description);
        Assert.Null(purchase.Merchant);
    }

    [Fact]
    public void Parse_CreditRow_HasPositiveAmount()
    {
        var parser = new Y12FcuParser();
        using var stream = OpenSample();

        var payment = parser.Parse(stream)
            .First(r => r.Description.StartsWith("From Share 00"));

        Assert.Equal(921.53m, payment.Amount);
    }

    [Fact]
    public void Parse_MissingHeaderRow_ThrowsImportException400()
    {
        var parser = new Y12FcuParser();
        var bytes = Encoding.UTF8.GetBytes("Some,Other,Headers\nrow,one,here\n");
        using var stream = new MemoryStream(bytes);

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Unrecognized CSV format", ex.Title);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsImportException400()
    {
        var parser = new Y12FcuParser();
        var csv = "Transaction Number,Date,Description,Balance\n" +
                  "abc,05/04/2026,Purchase,10.00\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Missing column", ex.Title);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new Y12FcuParser();
        var csv = "Transaction Number,Date,Description,Memo,Amount Debit,Amount Credit\n" +
                  "abc,05/04/2026,Purchase,MERCHANT,not-a-number,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Amount Debit", ex.Title);
    }

    [Fact]
    public void Parse_MalformedDate_ThrowsImportException400()
    {
        var parser = new Y12FcuParser();
        var csv = "Transaction Number,Date,Description,Memo,Amount Debit,Amount Credit\n" +
                  "abc,not-a-date,Purchase,MERCHANT,-10.00,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Date", ex.Title);
    }
}
