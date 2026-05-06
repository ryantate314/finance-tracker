using Transactatrack.Application.Imports;

namespace Transactatrack.UnitTests.Imports;

/// <summary>
/// Verifies the occurrence-suffix dedup logic that lives in ImportService.UploadAsync.
/// Extracted here as pure data-structure tests so we can cover edge cases without spinning
/// up a full database.
/// </summary>
public class OccurrenceDedupTests
{
    // Mirrors the logic in ImportService.UploadAsync — kept in sync manually.
    private static (List<string> hashes, List<string> baseHashes, List<int> ordinals) BuildHashes(
        IEnumerable<string> baseInputs)
    {
        var occurrence = new Dictionary<string, int>();
        var hashes = new List<string>();
        var baseHashes = new List<string>();
        var ordinals = new List<int>();
        foreach (var b in baseInputs)
        {
            var ord = occurrence.GetValueOrDefault(b, 0);
            occurrence[b] = ord + 1;
            hashes.Add(ord == 0 ? b : $"{b}#{ord}");
            baseHashes.Add(b);
            ordinals.Add(ord);
        }
        return (hashes, baseHashes, ordinals);
    }

    private static List<bool> RunDedupCheck(
        IEnumerable<string> csvBaseHashes, HashSet<string> existingSet)
    {
        var (hashes, baseHashes, ordinals) = BuildHashes(csvBaseHashes);
        var results = new List<bool>();
        for (var i = 0; i < hashes.Count; i++)
        {
            var isDup = existingSet.Contains(hashes[i]) ||
                        (ordinals[i] > 0 && existingSet.Contains(baseHashes[i]));
            results.Add(isDup);
        }
        return results;
    }

    [Fact]
    public void FreshImport_NoDuplicates()
    {
        // Empty DB — first upload of a CSV with two identical rows.
        var isDup = RunDedupCheck(["H1", "H1"], existingSet: []);
        Assert.Equal([false, false], isDup);
    }

    [Fact]
    public void ReImport_NewCodeData_BothAreDuplicates()
    {
        // DB was populated with new-code (occurrence suffix): H1 and H1#1 stored.
        var existingSet = new HashSet<string> { "H1", "H1#1" };
        var isDup = RunDedupCheck(["H1", "H1"], existingSet);
        Assert.Equal([true, true], isDup);
    }

    [Fact]
    public void ReImport_LegacyData_BothAreDuplicates()
    {
        // DB was populated with old-code (no suffix): only H1 stored for the pair.
        // On re-import with new code, the second row (H1#1) must still be detected
        // as a dup via the base-hash fallback.
        var existingSet = new HashSet<string> { "H1" };
        var isDup = RunDedupCheck(["H1", "H1"], existingSet);
        Assert.Equal([true, true], isDup);  // was [true, false] before the fix
    }

    [Fact]
    public void ReImport_LegacyData_MultipleCollisions_AllAreDuplicates()
    {
        // Fixture has FIXTURE DUP A (×2) and FIXTURE DUP B (×2) — 4 collision rows total.
        var existingSet = new HashSet<string> { "H_DUP_A", "H_DUP_B" }; // legacy: no suffix
        var csvBases = new[] { "H1", "H_DUP_A", "H_DUP_A", "H2", "H_DUP_B", "H_DUP_B" };
        var isDup = RunDedupCheck(csvBases, existingSet);
        // H1 and H2 are not in DB → new; the dup pairs all → dup
        Assert.Equal([false, true, true, false, true, true], isDup);
    }

    [Fact]
    public void UniqueRows_NeverMisidentifiedAsDuplicates()
    {
        // Rows with distinct base hashes should not be affected by the base-hash check.
        var existingSet = new HashSet<string> { "H1" };
        var isDup = RunDedupCheck(["H1", "H2", "H3"], existingSet);
        Assert.Equal([true, false, false], isDup);
    }
}
