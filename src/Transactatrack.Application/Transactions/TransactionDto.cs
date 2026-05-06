namespace Transactatrack.Application.Transactions;

public record TransactionDto(
    Guid Id,
    Guid AccountId,
    DateTime Date,
    DateTime? PostedDate,
    decimal Amount,
    string Description,
    string? Merchant,
    Guid? CategoryId,
    bool IsTransfer,
    Guid? TransferGroupId,
    Guid ImportBatchId,
    DateTime CreatedUtc
);
