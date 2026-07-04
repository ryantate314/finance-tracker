using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class TnBankParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "TnBankSample.csv"));

    [Fact]
    public void BankCode_IsTnBank()
    {
        var parser = new TnBankParser();
        Assert.Equal("TnBank", parser.BankCode);
    }

    [Fact]
    public void Parse_ReturnsFiveRows()
    {
        var parser = new TnBankParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void Parse_DebitRow_HasNegativeAmount()
    {
        var parser = new TnBankParser();
        using var stream = OpenSample();

        var debit = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), debit.Date);
        Assert.Null(debit.PostedDate);
        Assert.Equal(-70.50m, debit.Amount);
        Assert.Equal("SAMPLE INSURANCE PREM XXXXX0000", debit.Description);
        Assert.Null(debit.Merchant);
    }

    [Fact]
    public void Parse_CreditRow_HasPositiveAmount()
    {
        var parser = new TnBankParser();
        using var stream = OpenSample();

        var credit = parser.Parse(stream).First(r => r.Description.StartsWith("SAMPLE EMPLOYER"));

        Assert.Equal(3000.00m, credit.Amount);
    }

    [Fact]
    public void Parse_InterestCredit_CollapsesWhitespace()
    {
        var parser = new TnBankParser();
        using var stream = OpenSample();

        var interest = parser.Parse(stream).First(r => r.Description.StartsWith("INTEREST"));

        Assert.Equal(0.10m, interest.Amount);
        Assert.Equal("INTEREST AT .0293", interest.Description);
    }

    [Fact]
    public void Parse_SkipsRowsWithNoCreditOrDebit()
    {
        var parser = new TnBankParser();
        var csv = "Account,ChkRef,Debit,Credit,Balance,Date,Description\n" +
                  "000000,,,,,6/1/2026,MEMO ONLY\n" +
                  "000000,,10.00,,,6/2/2026,PURCHASE\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = parser.Parse(stream).ToList();

        Assert.Single(rows);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsImportException400()
    {
        var parser = new TnBankParser();
        var csv = "Account,Debit,Credit,Balance\n" +
                  "000000,10.00,,5000.00\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Missing column", ex.Title);
    }

    [Fact]
    public void Parse_MalformedAmount_ThrowsImportException400()
    {
        var parser = new TnBankParser();
        var csv = "Account,ChkRef,Debit,Credit,Balance,Date,Description\n" +
                  "000000,,not-a-number,,,6/1/2026,PURCHASE\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Debit", ex.Title);
    }

    [Fact]
    public void Parse_MalformedDate_ThrowsImportException400()
    {
        var parser = new TnBankParser();
        var csv = "Account,ChkRef,Debit,Credit,Balance,Date,Description\n" +
                  "000000,,10.00,,,not-a-date,PURCHASE\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Date", ex.Title);
    }
}
