using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class Y12FcuParser : IBankStatementParser
{
    private static readonly string[] RequiredHeaders =
        ["Date", "Description", "Memo", "Amount Debit", "Amount Credit"];

    // Y12FCU exports prepend three metadata lines (Account Name / Account Number /
    // Date Range) before the real CSV header row, which begins with this column.
    private const string HeaderMarker = "Transaction Number";

    public string BankCode => "Y12FCU";

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
        var content = reader.ReadToEnd();

        // Strip the metadata preamble by seeking to the header row.
        var headerIndex = content.IndexOf(HeaderMarker, StringComparison.OrdinalIgnoreCase);
        if (headerIndex < 0)
            throw new ImportException(400, $"Unrecognized CSV format for Y12FCU parser. Missing '{HeaderMarker}' header row.");

        using var csvReader = new StringReader(content[headerIndex..]);
        using var parser = new CsvReader(csvReader, config);

        if (!parser.Read() || !parser.ReadHeader())
            throw new ImportException(400, "CSV is empty or missing a header row.");

        var headers = parser.HeaderRecord ?? [];
        var missing = RequiredHeaders
            .Where(h => !headers.Contains(h, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count > 0)
            throw new ImportException(400, $"Unrecognized CSV format for Y12FCU parser. Missing column(s): {string.Join(", ", missing)}.");

        var rowNumber = 1;
        while (parser.Read())
        {
            rowNumber++;

            var rawDate = parser.GetField("Date");
            if (string.IsNullOrWhiteSpace(rawDate)) continue;

            // Amounts arrive in separate debit/credit columns. Rows with neither
            // (e.g. informational "COMMENT" / pending-credit lines) carry no money — skip them.
            var debit = parser.GetField("Amount Debit");
            var credit = parser.GetField("Amount Credit");
            if (string.IsNullOrWhiteSpace(debit) && string.IsNullOrWhiteSpace(credit))
                continue;

            DateTime date = ParseDate(rawDate)
                ?? throw new ImportException(400, $"Row {rowNumber}: 'Date' is not in MM/DD/YYYY format ('{rawDate}').");

            decimal amount = ParseAmount(debit, credit, rowNumber);

            // The "Description" column is a generic transaction type ("Purchase CREDIT CARD");
            // "Memo" holds the merchant detail that categorization keys off of. Prefer Memo.
            var memo = CollapseWhitespace(parser.GetField("Memo") ?? string.Empty);
            var typeDescription = CollapseWhitespace(parser.GetField("Description") ?? string.Empty);
            var description = memo.Length > 0 ? memo : typeDescription;

            yield return new ParsedTransaction(
                Date: date,
                PostedDate: null,
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

    private static decimal ParseAmount(string? debit, string? credit, int rowNumber)
    {
        // Debits are outflows (negative), credits are inflows (positive). Normalize the
        // magnitude and apply the sign by column so we don't depend on the export's own sign.
        if (!string.IsNullOrWhiteSpace(debit))
            return -Math.Abs(ParseDecimal(debit, rowNumber, "Amount Debit"));
        return Math.Abs(ParseDecimal(credit, rowNumber, "Amount Credit"));
    }

    private static decimal ParseDecimal(string? value, int rowNumber, string fieldName)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw new ImportException(400, $"Row {rowNumber}: '{fieldName}' is not a valid number ('{value}').");
        return amount;
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
