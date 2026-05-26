using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports.Parsers;

public class ChaseParser : IBankStatementParser
{
    private static readonly string[] RequiredHeaders =
        ["Transaction Date", "Post Date", "Description", "Amount"];

    public string BankCode => "Chase";

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
            throw new ImportException(400, $"Unrecognized CSV format for Chase parser. Missing column(s): {string.Join(", ", missing)}.");

        var rowNumber = 1;
        while (parser.Read())
        {
            rowNumber++;

            var rawDate = parser.GetField("Transaction Date");
            if (string.IsNullOrWhiteSpace(rawDate)) continue;

            DateTime date;
            try
            {
                date = ParseDate(rawDate)
                    ?? throw new ImportException(400, $"Row {rowNumber}: 'Transaction Date' is not in MM/DD/YYYY format ('{rawDate}').");
            }
            catch (ImportException) { throw; }
            catch (Exception ex)
            {
                throw new ImportException(400, $"Row {rowNumber}: failed to parse Transaction Date — {ex.Message}");
            }

            var postedDate = ParseDate(parser.GetField("Post Date"));

            decimal amount;
            try
            {
                amount = ParseAmount(parser.GetField("Amount"), rowNumber);
            }
            catch (ImportException) { throw; }
            catch (Exception ex)
            {
                throw new ImportException(400, $"Row {rowNumber}: failed to parse Amount — {ex.Message}");
            }

            var description = parser.GetField("Description")?.Trim() ?? string.Empty;

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
        return amount;
    }
}
