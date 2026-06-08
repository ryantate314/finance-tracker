using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class DiscoverParser : IBankStatementParser
{
    private static readonly string[] RequiredHeaders =
        ["Trans. Date", "Post Date", "Description", "Amount"];

    public string BankCode => "Discover";

    public IEnumerable<ParsedTransaction> Parse(Stream stream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        // leaveOpen: caller (ImportService) owns the stream and disposes it.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
        using var parser = new CsvReader(reader, config);

        if (!parser.Read() || !parser.ReadHeader())
            throw new ImportException(400, "CSV is empty or missing a header row.");

        var headers = parser.HeaderRecord ?? [];
        var missing = RequiredHeaders
            .Where(h => !headers.Contains(h, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count > 0)
            throw new ImportException(400, $"Unrecognized CSV format for Discover parser. Missing column(s): {string.Join(", ", missing)}.");

        var rowNumber = 1;
        while (parser.Read())
        {
            rowNumber++;

            var rawDate = parser.GetField("Trans. Date");
            if (string.IsNullOrWhiteSpace(rawDate)) continue;

            DateTime date = ParseDate(rawDate)
                ?? throw new ImportException(400, $"Row {rowNumber}: 'Trans. Date' is not in MM/DD/YYYY format ('{rawDate}').");

            DateTime? postedDate = ParseDate(parser.GetField("Post Date"));

            decimal amount = ParseAmount(parser.GetField("Amount"), rowNumber);

            var description = CollapseWhitespace(parser.GetField("Description") ?? string.Empty);

            yield return new ParsedTransaction(
                Date: date,
                PostedDate: postedDate,
                Amount: amount,
                Description: description,
                Merchant: null);
        }
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(
            value,
            "MM/dd/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static decimal ParseAmount(string? value, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ImportException(400, $"Row {rowNumber}: 'Amount' is empty.");
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw new ImportException(400, $"Row {rowNumber}: 'Amount' is not a valid number ('{value}').");

        // Discover reports purchases as positive and payments/credits as negative — the
        // inverse of our canonical ledger (outflows negative, inflows positive). Flip the
        // sign so the column matches every other parser.
        return -amount;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
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
