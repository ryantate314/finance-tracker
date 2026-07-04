using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transactatrack.Application.Transfers;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Infrastructure.Transfers;

/// <summary>
/// Pairs equal-and-opposite transactions across two accounts in the same family within a
/// configurable date window, marking both legs as a transfer joined by a shared
/// <see cref="Transaction.TransferGroupId"/>. All queries run under the active-family EF
/// query filter, so everything here is implicitly family-scoped.
/// </summary>
public class TransferMatcher : ITransferMatcher
{
    private readonly AppDbContext _db;
    private readonly TransferMatchOptions _options;
    private readonly ILogger<TransferMatcher> _logger;

    public TransferMatcher(AppDbContext db, IOptions<TransferMatchOptions> options, ILogger<TransferMatcher> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransferMatchResult> MatchBatchAsync(Guid batchId, CancellationToken ct)
    {
        var batchRowIds = await _db.Transactions
            .Where(t => t.ImportBatchId == batchId && t.TransferGroupId == null)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (batchRowIds.Count == 0) return new TransferMatchResult(0, 0);

        return await MatchCoreAsync(batchRowIds.ToHashSet(), ct);
    }

    public Task<TransferMatchResult> RescanFamilyAsync(CancellationToken ct) => MatchCoreAsync(null, ct);

    /// <summary>
    /// Core greedy matcher. When <paramref name="touchedRows"/> is non-null, only pairs with at
    /// least one leg in that set are created (so a batch import doesn't re-pair the whole ledger);
    /// when null, every unpaired candidate is fair game (full rescan).
    /// </summary>
    private async Task<TransferMatchResult> MatchCoreAsync(HashSet<Guid>? touchedRows, CancellationToken ct)
    {
        // Candidate = committed, unpaired, and either uncategorized or already Transfer-kind.
        // Rows the user/rules categorized as real spend are never auto-matched.
        var pool = await (
            from t in _db.Transactions
            join b in _db.ImportBatches on t.ImportBatchId equals b.Id
            where b.Status == ImportBatchStatus.Committed && t.TransferGroupId == null
            join c in _db.Categories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where t.CategoryId == null || c.Kind == CategoryKind.Transfer
            select t)
            .ToListAsync(ct);

        if (pool.Count == 0) return new TransferMatchResult(0, 0);

        Guid transferCatId = await _db.Categories
            .Where(c => c.Kind == CategoryKind.Transfer)
            .Select(c => c.Id)
            .FirstAsync(ct);

        // Deterministic order so ambiguous clusters always pair the same way across runs.
        var ordered = pool
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Amount)
            .ThenBy(t => t.Id)
            .ToList();
        var byAmount = ordered.ToLookup(t => t.Amount);

        var paired = new HashSet<Guid>();
        int pairs = 0, scanned = 0;

        // Drive from the outflow leg (Amount < 0) so each pair is only processed once.
        foreach (var a in ordered)
        {
            if (a.Amount >= 0 || paired.Contains(a.Id)) continue;
            scanned++;

            Transaction? best = null;
            int bestDelta = int.MaxValue;
            foreach (var b in byAmount[-a.Amount])
            {
                if (b.Id == a.Id || paired.Contains(b.Id) || b.AccountId == a.AccountId) continue;
                int delta = Math.Abs((b.Date.Date - a.Date.Date).Days);
                if (delta > _options.WindowDays) continue;
                if (delta < bestDelta || (delta == bestDelta && best is not null && b.Id.CompareTo(best.Id) < 0))
                {
                    best = b;
                    bestDelta = delta;
                }
            }

            if (best is null) continue;

            // For a batch run, only commit pairs that actually involve the new rows.
            if (touchedRows is not null && !touchedRows.Contains(a.Id) && !touchedRows.Contains(best.Id))
                continue;

            Guid groupId = Guid.NewGuid();
            MarkAsTransfer(a, transferCatId, groupId);
            MarkAsTransfer(best, transferCatId, groupId);
            paired.Add(a.Id);
            paired.Add(best.Id);
            pairs++;
        }

        if (pairs > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("TransferMatcher paired {Pairs} transfer(s) from {Scanned} outflow candidate(s).", pairs, scanned);
        }

        return new TransferMatchResult(pairs, scanned);
    }

    public async Task<Guid> LinkAsync(Guid txAId, Guid txBId, CancellationToken ct)
    {
        if (txAId == txBId)
            throw new TransferException(400, "Cannot link a transaction to itself.");

        var a = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == txAId, ct)
            ?? throw new TransferException(404, $"Transaction {txAId} not found.");
        var b = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == txBId, ct)
            ?? throw new TransferException(404, $"Transaction {txBId} not found.");

        if (a.AccountId == b.AccountId)
            throw new TransferException(400, "Both transactions belong to the same account.");
        if (a.TransferGroupId is not null || b.TransferGroupId is not null)
            throw new TransferException(409, "One or both transactions are already part of a transfer.");

        if (a.Amount + b.Amount != 0)
            _logger.LogWarning("Manual transfer link {A}+{B} does not net to zero ({SumA} + {SumB}).",
                a.Id, b.Id, a.Amount, b.Amount);

        Guid transferCatId = await _db.Categories
            .Where(c => c.Kind == CategoryKind.Transfer)
            .Select(c => c.Id)
            .FirstAsync(ct);

        Guid groupId = Guid.NewGuid();
        MarkAsTransfer(a, transferCatId, groupId);
        MarkAsTransfer(b, transferCatId, groupId);
        await _db.SaveChangesAsync(ct);
        return groupId;
    }

    public async Task UnlinkAsync(Guid groupId, CancellationToken ct)
    {
        var legs = await _db.Transactions.Where(t => t.TransferGroupId == groupId).ToListAsync(ct);
        if (legs.Count == 0)
            throw new TransferException(404, $"No transfer group {groupId} found.");

        Guid transferCatId = await _db.Categories
            .Where(c => c.Kind == CategoryKind.Transfer)
            .Select(c => c.Id)
            .FirstAsync(ct);

        foreach (var t in legs)
        {
            t.TransferGroupId = null;
            t.IsTransfer = false;
            // Only revert the category we auto-assigned; leave a user's own categorization intact.
            if (t.CategoryId == transferCatId)
            {
                t.CategoryId = null;
                t.SubCategoryId = null;
                t.CategorizationSource = CategorizationSource.Manual;
                t.CategorizedUtc = null;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stamp a leg as a transfer. Assigns the system Transfer category unless the row already
    /// carries a manual non-transfer categorization (which the boundary logic respects via
    /// <c>TransferGroupId</c> regardless of category).
    /// </summary>
    private static void MarkAsTransfer(Transaction t, Guid transferCatId, Guid groupId)
    {
        t.TransferGroupId = groupId;
        t.IsTransfer = true;

        bool manualOtherCategory =
            t.CategorizationSource == CategorizationSource.Manual &&
            t.CategoryId is not null &&
            t.CategoryId != transferCatId;

        if (!manualOtherCategory)
        {
            t.CategoryId = transferCatId;
            t.SubCategoryId = null;
            t.CategorizationSource = CategorizationSource.Manual;
            t.CategorizedUtc = DateTime.UtcNow;
        }
    }
}
