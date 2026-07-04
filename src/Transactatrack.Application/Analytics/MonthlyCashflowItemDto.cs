namespace Transactatrack.Application.Analytics;

public record MonthlyCashflowItemDto(
    int Year,
    int Month,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal TransfersIn,
    decimal TransfersOut
);
