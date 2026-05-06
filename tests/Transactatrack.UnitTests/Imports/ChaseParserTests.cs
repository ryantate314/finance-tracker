using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class ChaseParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "ChaseSample.csv"));

    [Fact]
    public void BankCode_IsChase()
    {
        var parser = new ChaseParser();
        Assert.Equal("Chase", parser.BankCode);
    }

    [Fact]
    public void Parse_SampleFile_Returns176Rows()
    {
        var parser = new ChaseParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.Equal(176, rows.Count);
    }

    [Fact]
    public void Parse_FirstRow_HasExpectedValues()
    {
        var parser = new ChaseParser();
        using var stream = OpenSample();

        var first = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), first.Date);
        Assert.Equal(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), first.PostedDate);
        Assert.Equal("FIXTURE PAYMENT", first.Description);
        Assert.Equal(500.00m, first.Amount);
        Assert.Null(first.Merchant);
    }

    [Fact]
    public void Parse_NegativeAmounts_PreserveSign()
    {
        var parser = new ChaseParser();
        using var stream = OpenSample();

        var negative = parser.Parse(stream)
            .First(r => r.Description == "FIXTURE NEGATIVE");

        Assert.Equal(-100.00m, negative.Amount);
    }

    [Fact]
    public void Parse_DifferentTransactionAndPostDates_BothPopulated()
    {
        var parser = new ChaseParser();
        using var stream = OpenSample();

        var gas = parser.Parse(stream)
            .First(r => r.Description == "FIXTURE GAS");

        Assert.Equal(new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), gas.Date);
        Assert.Equal(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc), gas.PostedDate);
    }

    [Fact]
    public void Parse_MissingRequiredHeaders_ThrowsImportException400()
    {
        var parser = new ChaseParser();
        var bytes = Encoding.UTF8.GetBytes("Some,Other,Headers\nrow,one,here\n");
        using var stream = new MemoryStream(bytes);

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Unrecognized CSV format", ex.Title);
    }

    [Fact]
    public void Parse_EmptyFile_ThrowsImportException400()
    {
        var parser = new ChaseParser();
        using var stream = new MemoryStream(Array.Empty<byte>());

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new ChaseParser();
        var csv = "Transaction Date,Post Date,Description,Category,Type,Amount,Memo\n" +
                  "05/04/2026,05/04/2026,GOOD ROW,,Sale,-10.00,\n" +
                  "05/05/2026,05/05/2026,BAD ROW,,Sale,not-a-number,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Amount", ex.Title);
    }

    [Fact]
    public void Parse_MalformedDate_ThrowsImportException400()
    {
        var parser = new ChaseParser();
        var csv = "Transaction Date,Post Date,Description,Category,Type,Amount,Memo\n" +
                  "not-a-date,05/05/2026,BAD ROW,,Sale,-10.00,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Transaction Date", ex.Title);
    }
}
