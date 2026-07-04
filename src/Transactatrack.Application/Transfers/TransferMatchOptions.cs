namespace Transactatrack.Application.Transfers;

/// <summary>
/// Configuration for <see cref="ITransferMatcher"/>, bound from the "Transfers" config section.
/// </summary>
public sealed record TransferMatchOptions
{
    /// <summary>Maximum absolute day difference between the two legs of a transfer pair.</summary>
    public int WindowDays { get; init; } = 3;
}
