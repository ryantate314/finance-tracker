namespace Transactatrack.Application.Imports;

public record ParsedTransaction(
    DateTime Date,
    DateTime? PostedDate,
    decimal Amount,
    string Description,
    string? Merchant
);
