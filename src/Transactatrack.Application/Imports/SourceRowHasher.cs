using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Transactatrack.Application.Imports;

public partial class SourceRowHasher
{
    public string Hash(Guid accountId, DateTime date, decimal amount, string description)
    {
        var canonical = string.Join('|',
            accountId.ToString("N"),
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount.ToString("0.0000", CultureInfo.InvariantCulture),
            Normalize(description));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string description) =>
        WhitespaceRegex().Replace(description.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
