using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports;

public class BankParserRegistry : IBankParserRegistry
{
    private readonly Dictionary<string, IBankCsvParser> _parsers;

    public BankParserRegistry(IEnumerable<IBankCsvParser> parsers)
    {
        _parsers = parsers.ToDictionary(p => p.BankCode, StringComparer.OrdinalIgnoreCase);
    }

    public IBankCsvParser? Get(string bankCode) =>
        _parsers.TryGetValue(bankCode, out var parser) ? parser : null;
}
