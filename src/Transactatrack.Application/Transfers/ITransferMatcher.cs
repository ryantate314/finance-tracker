namespace Transactatrack.Application.Transfers;

public interface ITransferMatcher
{
    /// <summary>
    /// Pair the new rows of a freshly-committed batch against the rest of the committed
    /// family ledger. Only pairs that touch <paramref name="batchId"/> are created.
    /// </summary>
    Task<TransferMatchResult> MatchBatchAsync(Guid batchId, CancellationToken ct);

    /// <summary>Re-scan all committed, currently-unpaired transfer candidates in the family.</summary>
    Task<TransferMatchResult> RescanFamilyAsync(CancellationToken ct);

    /// <summary>
    /// Manually link two transactions as a transfer (escape hatch — no equal-and-opposite
    /// requirement). Returns the new <c>TransferGroupId</c>.
    /// </summary>
    Task<Guid> LinkAsync(Guid txAId, Guid txBId, CancellationToken ct);

    /// <summary>Break a transfer group: clears the link on every leg and reverts auto-assigned categories.</summary>
    Task UnlinkAsync(Guid groupId, CancellationToken ct);
}
