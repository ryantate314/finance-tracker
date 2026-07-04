using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class FidelityParser : IBankStatementParser
{
    private static readonly string[] RequiredHeaders =
        ["Run Date", "Action", "Amount ($)"];

    public string BankCode => "Fidelity";

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
            throw new ImportException(400, $"Unrecognized CSV format for Fidelity parser. Missing column(s): {string.Join(", ", missing)}.");

        var rowNumber = 1;
        while (parser.Read())
        {
            rowNumber++;

            // Blank rows and footer disclaimer rows both produce unparseable dates — skip quietly.
            var rawDate = parser.GetField("Run Date");
            DateTime? date = ParseDate(rawDate);
            if (date == null) continue;

            // Money-market reinvestments are automatic sweeps paired with a matching DIVIDEND
            // RECEIVED row; they net to zero and add noise to the ledger.
            var action = parser.GetField("Action") ?? string.Empty;
            if (action.StartsWith("REINVESTMENT", StringComparison.OrdinalIgnoreCase)) continue;

            var rawAmount = parser.GetField("Amount ($)");
            if (string.IsNullOrWhiteSpace(rawAmount)) continue;

            decimal amount = ParseDecimal(rawAmount, rowNumber);
            var description = StripCashSuffix(CollapseWhitespace(action));

            yield return new ParsedTransaction(
                Date: date.Value,
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
            "MM-dd-yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static decimal ParseDecimal(string? value, int rowNumber)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw new ImportException(400, $"Row {rowNumber}: 'Amount ($)' is not a valid number ('{value}').");
        return amount;
    }

    private static string StripCashSuffix(string action)
    {
        const string suffix = " (Cash)";
        return action.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? action[..^suffix.Length]
            : action;
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
