using Transactatrack.Application.Imports;

namespace Transactatrack.Infrastructure.Imports;

public class BankParserRegistry : IBankParserRegistry
{
    private readonly Dictionary<string, IBankStatementParser> _parsers;

    public BankParserRegistry(IEnumerable<IBankStatementParser> parsers)
    {
        _parsers = parsers.ToDictionary(p => p.BankCode, StringComparer.OrdinalIgnoreCase);
    }

    public IBankStatementParser? Get(string bankCode) =>
        _parsers.TryGetValue(bankCode, out var parser) ? parser : null;

    public IReadOnlyCollection<string> BankCodes =>
        _parsers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
}
