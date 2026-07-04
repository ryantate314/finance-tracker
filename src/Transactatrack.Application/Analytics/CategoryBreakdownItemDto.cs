namespace Transactatrack.Application.Analytics;

public record CategoryBreakdownItemDto(
    Guid? CategoryId,
    string CategoryName,
    decimal Amount,
    int TransactionCount,
    bool IsTransfersBucket = false
);
