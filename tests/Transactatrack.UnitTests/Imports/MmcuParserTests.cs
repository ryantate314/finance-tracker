using System.Text;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;

namespace Transactatrack.UnitTests.Imports;

public class MmcuParserTests
{
    private static Stream OpenSample() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "MmcuSample.html"));

    [Fact]
    public void BankCode_IsMmcu()
    {
        var parser = new MmcuParser();
        Assert.Equal("MMCU", parser.BankCode);
    }

    [Fact]
    public void Parse_SampleFile_ReturnsFiveRows()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var rows = parser.Parse(stream).ToList();

        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void Parse_FirstRow_HasExpectedValues()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var first = parser.Parse(stream).First();

        Assert.Equal(new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc), first.Date);
        Assert.Equal(new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc), first.PostedDate);
        Assert.Equal("ACH Transaction/EFT-VENMO/CASHOUT", first.Description);
        Assert.Equal(214.00m, first.Amount);
        Assert.Null(first.Merchant);
    }

    [Fact]
    public void Parse_NegativeAmount_ParsedFromParens()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var draft = parser.Parse(stream)
            .First(r => r.Description.StartsWith("Share Draft/TR:"));

        Assert.Equal(-1250.00m, draft.Amount);
    }

    [Fact]
    public void Parse_ShareDraftRow_PreservesCheckReferenceInDescription()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var draft = parser.Parse(stream)
            .First(r => r.Description.Contains("TR:"));

        Assert.Equal("Share Draft/TR:0090710502", draft.Description);
    }

    [Fact]
    public void Parse_RowWithDifferentEffectiveDate_SplitsDates()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var weekendPost = parser.Parse(stream)
            .First(r => r.Description.StartsWith("Debit Card Purchase"));

        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), weekendPost.Date);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), weekendPost.PostedDate);
    }

    [Fact]
    public void Parse_DescriptionCollapsesInternalWhitespace()
    {
        var parser = new MmcuParser();
        using var stream = OpenSample();

        var atm = parser.Parse(stream)
            .First(r => r.Description.StartsWith("ATM Posting"));

        Assert.Equal("ATM Posting/ATM- 0/41--VALE RD US-TN MARYVILLE", atm.Description);
    }

    [Fact]
    public void Parse_HtmlWithoutPostedTable_ThrowsImportException400()
    {
        var parser = new MmcuParser();
        var html = "<!DOCTYPE html><html><body><p>no table here</p></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Unrecognized MMCU HTML", ex.Title);
    }

    [Fact]
    public void Parse_EmptyFile_ThrowsImportException400()
    {
        var parser = new MmcuParser();
        using var stream = new MemoryStream(Array.Empty<byte>());

        var ex = Assert.Throws<ImportException>(() => parser.Parse(stream).ToList());

        Assert.Equal(400, ex.StatusCode);
    }
}
