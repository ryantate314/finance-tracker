using System.Globalization;
using HtmlAgilityPack;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class MmcuParser : IBankStatementParser
{
    public string BankCode => "MMCU";

    public IEnumerable<ParsedTransaction> Parse(Stream stream)
    {
        var doc = new HtmlDocument();
        doc.Load(stream);

        HtmlNodeCollection? rows = doc.DocumentNode.SelectNodes("//table[@id='tblPosted']/tbody/tr");
        if (rows is null || rows.Count == 0)
            throw new ImportException(400, "Unrecognized MMCU HTML: missing or empty <table id='tblPosted'>.");

        int rowNumber = 0;
        foreach (HtmlNode tr in rows)
        {
            rowNumber++;

            HtmlNodeCollection? tds = tr.SelectNodes("./td");
            if (tds is null || tds.Count < 6)
                throw new ImportException(400, $"Row {rowNumber}: expected 6 <td> cells, found {tds?.Count ?? 0}.");

            DateTime postedDate = ParseDate(tds[1].InnerText, rowNumber, "Posted Date");
            string description = ExtractLeadingText(tds[2]);
            DateTime effectiveDate = ExtractEffectiveDate(tds[2]) ?? postedDate;
            decimal amount = ParseAmount(tds[4], rowNumber);

            yield return new ParsedTransaction(
                Date: effectiveDate,
                PostedDate: postedDate,
                Amount: amount,
                Description: description,
                Merchant: null);
        }
    }

    private static DateTime ParseDate(string? value, int rowNumber, string fieldName)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ImportException(400, $"Row {rowNumber}: '{fieldName}' is empty.");
        if (!DateTime.TryParseExact(
                trimmed,
                "MM/dd/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            throw new ImportException(400, $"Row {rowNumber}: '{fieldName}' is not in MM/DD/YYYY format ('{trimmed}').");
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    private static string ExtractLeadingText(HtmlNode td)
    {
        // The description cell looks like:
        //   <td>
        //     ACH Transaction/EFT-VENMO/CASHOUT         <-- leading text node
        //     <div class="it-details-row collapse">…</div>
        //   </td>
        // We want only the leading text — the <div> holds the collapsible details panel.
        foreach (HtmlNode child in td.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Text) continue;
            string text = HtmlEntity.DeEntitize(child.InnerText).Trim();
            if (text.Length > 0)
                return CollapseWhitespace(text);
        }
        return string.Empty;
    }

    private static DateTime? ExtractEffectiveDate(HtmlNode td)
    {
        HtmlNode? dd = td.SelectSingleNode(
            ".//dt[normalize-space()='Effective Date']/following-sibling::dd[1]");
        if (dd is null) return null;

        string text = HtmlEntity.DeEntitize(dd.InnerText).Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return DateTime.TryParseExact(
            text,
            "MM/dd/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static decimal ParseAmount(HtmlNode td, int rowNumber)
    {
        HtmlNode? span = td.SelectSingleNode(".//span[contains(@class,'positive') or contains(@class,'negative')]");
        if (span is null)
            throw new ImportException(400, $"Row {rowNumber}: 'Amount' cell has no positive/negative span.");

        string raw = HtmlEntity.DeEntitize(span.InnerText).Trim();
        if (string.IsNullOrEmpty(raw))
            throw new ImportException(400, $"Row {rowNumber}: 'Amount' is empty.");

        bool isNegative = raw.StartsWith('(') && raw.EndsWith(')');
        string cleaned = raw
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("$", string.Empty)
            .Replace(",", string.Empty)
            .Trim();

        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw new ImportException(400, $"Row {rowNumber}: 'Amount' is not a valid number ('{raw}').");

        return isNegative ? -amount : amount;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool prevWs = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevWs) sb.Append(' ');
                prevWs = true;
            }
            else
            {
                sb.Append(c);
                prevWs = false;
            }
        }
        return sb.ToString().Trim();
    }
}
