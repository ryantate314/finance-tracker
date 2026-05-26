namespace Transactatrack.Application.Imports;

public interface IBankParserRegistry
{
    IBankStatementParser? Get(string bankCode);
    IReadOnlyCollection<string> BankCodes { get; }
}
