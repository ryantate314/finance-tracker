namespace Transactatrack.Application.Imports;

public interface IBankCsvParser
{
    string BankCode { get; }
    IEnumerable<ParsedTransaction> Parse(Stream csv);
}
