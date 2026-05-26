namespace Transactatrack.Application.Imports;

public interface IBankStatementParser
{
    string BankCode { get; }
    IEnumerable<ParsedTransaction> Parse(Stream stream);
}
