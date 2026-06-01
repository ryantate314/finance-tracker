using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class FirstHorizonParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "FirstHorizonSample.csv"));

    [Fact]
    public void BankCode_IsFirstHorizon()
    {
        var parser = new FirstHorizonParser();
        Assert.Equal("FirstHorizon", parser.BankCode);
    }

    [Fact]
    public void Parse_SkipsRowsWithNoCreditOrDebit()
    {
        var parser = new FirstHorizonParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        // 6 money rows; the informational row with empty Credit and Debit is dropped.
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public void Parse_DebitRow_HasNegativeAmount()
    {
        var parser = new FirstHorizonParser();
        using var stream = OpenSample();

        var debit = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc), debit.Date);
        Assert.Null(debit.PostedDate);
        Assert.Equal(-1520.00m, debit.Amount);
        Assert.Equal("SAMPLE DEBIT XXXXXXSAMPLE HOLDER, NAME", debit.Description);
        Assert.Null(debit.Merchant);
    }

    [Fact]
    public void Parse_CreditRow_HasPositiveAmount()
    {
        var parser = new FirstHorizonParser();
        using var stream = OpenSample();

        var credit = parser.Parse(stream)
            .First(r => r.Description.StartsWith("SAMPLE EMPLOYER"));

        Assert.Equal(2490.00m, credit.Amount);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsImportException400()
    {
        var parser = new FirstHorizonParser();
        var csv = "\"Date\",\"Description\",\"Balance\"\n" +
                  "\"05/04/2026\",\"Purchase\",\"10.00\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Missing column", ex.Title);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new FirstHorizonParser();
        var csv = "\"Date\",\"Description\",\"Credit\",\"Debit\"\n" +
                  "\"05/04/2026\",\"Purchase\",\"\",\"not-a-number\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Debit", ex.Title);
    }

    [Fact]
    public void Parse_MalformedDate_ThrowsImportException400()
    {
        var parser = new FirstHorizonParser();
        var csv = "\"Date\",\"Description\",\"Credit\",\"Debit\"\n" +
                  "\"not-a-date\",\"Purchase\",\"\",\"-10.00\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Date", ex.Title);
    }
}
