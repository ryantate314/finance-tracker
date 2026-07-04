using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class TnBankParser : IBankStatementParser
{
    private static readonly string[] RequiredHeaders =
        ["Debit", "Credit", "Date", "Description"];

    public string BankCode => "TnBank";

    public IEnumerable<ParsedTransaction> Parse(Stream stream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
        using var parser = new CsvReader(reader, config);

        if (!parser.Read() || !parser.ReadHeader())
            throw new ImportException(400, "CSV is empty or missing a header row.");

        var headers = parser.HeaderRecord ?? [];
        var missing = RequiredHeaders
            .Where(h => !headers.Contains(h, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count > 0)
            throw new ImportException(400, $"Unrecognized CSV format for TnBank parser. Missing column(s): {string.Join(", ", missing)}.");

        var rowNumber = 1;
        while (parser.Read())
        {
            rowNumber++;

            var rawDate = parser.GetField("Date");
            if (string.IsNullOrWhiteSpace(rawDate)) continue;

            var debit = parser.GetField("Debit");
            var credit = parser.GetField("Credit");
            if (string.IsNullOrWhiteSpace(debit) && string.IsNullOrWhiteSpace(credit))
                continue;

            DateTime date = ParseDate(rawDate)
                ?? throw new ImportException(400, $"Row {rowNumber}: 'Date' is not in M/d/yyyy format ('{rawDate}').");

            decimal amount = ParseAmount(debit, credit, rowNumber);

            var description = CollapseWhitespace(parser.GetField("Description") ?? string.Empty);

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
            ["M/d/yyyy", "MM/dd/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static decimal ParseAmount(string? debit, string? credit, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(debit))
            return -Math.Abs(ParseDecimal(debit, rowNumber, "Debit"));
        return Math.Abs(ParseDecimal(credit, rowNumber, "Credit"));
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
