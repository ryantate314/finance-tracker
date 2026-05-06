namespace Transactatrack.Application.Imports;

public interface IBankParserRegistry
{
    IBankCsvParser? Get(string bankCode);
}
