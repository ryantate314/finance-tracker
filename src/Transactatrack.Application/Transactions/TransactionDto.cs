using Transactatrack.Domain.Enums;

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
    Guid? SubCategoryId,
    bool IsTransfer,
    Guid? TransferGroupId,
    Guid ImportBatchId,
    DateTime CreatedUtc,
    CategorizationSource CategorizationSource,
    bool NeedsReview,
    decimal? LlmConfidence,
    Guid? AppliedRuleId,
    string SourceRowHash = "",
    string? LlmModel = null,
    DateTime? CategorizedUtc = null,
    string? Note = null
);
