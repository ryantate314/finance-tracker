using Transactatrack.Application.Imports;

namespace Transactatrack.UnitTests.Imports;

public class SourceRowHasherTests
{
    private readonly SourceRowHasher _hasher = new();

    [Fact]
    public void Hash_IsDeterministic()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var a = _hasher.Hash(accountId, date, -12.50m, "AMAZON MKTPL*BJ9DV7UK1");
        var b = _hasher.Hash(accountId, date, -12.50m, "AMAZON MKTPL*BJ9DV7UK1");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_Length_IsSha256Hex()
    {
        var hash = _hasher.Hash(Guid.NewGuid(), DateTime.UtcNow, 1m, "x");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_Description_IsCaseInsensitive()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var lower = _hasher.Hash(accountId, date, 1m, "amazon mktpl");
        var upper = _hasher.Hash(accountId, date, 1m, "AMAZON MKTPL");

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void Hash_Description_IgnoresLeadingTrailingWhitespace()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var trimmed = _hasher.Hash(accountId, date, 1m, "AMAZON");
        var padded = _hasher.Hash(accountId, date, 1m, "  AMAZON  ");

        Assert.Equal(trimmed, padded);
    }

    [Fact]
    public void Hash_Description_CollapsesInternalWhitespace()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var single = _hasher.Hash(accountId, date, 1m, "AMAZON MKTPL");
        var multi = _hasher.Hash(accountId, date, 1m, "AMAZON     MKTPL");

        Assert.Equal(single, multi);
    }

    [Fact]
    public void Hash_DifferentDates_ProduceDifferentHashes()
    {
        var accountId = Guid.NewGuid();

        var d1 = _hasher.Hash(accountId, new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), 1m, "x");
        var d2 = _hasher.Hash(accountId, new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), 1m, "x");

        Assert.NotEqual(d1, d2);
    }

    [Fact]
    public void Hash_DifferentAmounts_ProduceDifferentHashes()
    {
        var accountId = Guid.NewGuid();
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var a1 = _hasher.Hash(accountId, date, 1m, "x");
        var a2 = _hasher.Hash(accountId, date, 1.0001m, "x");

        Assert.NotEqual(a1, a2);
    }

    [Fact]
    public void Hash_DifferentAccounts_ProduceDifferentHashes()
    {
        var date = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        var a1 = _hasher.Hash(Guid.NewGuid(), date, 1m, "x");
        var a2 = _hasher.Hash(Guid.NewGuid(), date, 1m, "x");

        Assert.NotEqual(a1, a2);
    }
}
