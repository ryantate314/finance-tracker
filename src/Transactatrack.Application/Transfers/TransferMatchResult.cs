namespace Transactatrack.Application.Transfers;

/// <summary>Outcome of a transfer-matching run.</summary>
/// <param name="Paired">Number of transfer pairs created.</param>
/// <param name="Scanned">Number of outflow candidate legs considered.</param>
public record TransferMatchResult(int Paired, int Scanned);
