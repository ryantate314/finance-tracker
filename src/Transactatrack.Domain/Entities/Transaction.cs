using Transactatrack.Domain.Common;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Domain.Entities;

public class Transaction : FamilyScopedEntity
{
    public Guid AccountId { get; set; }
    public DateTime Date { get; set; }
    public DateTime? PostedDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string? Note { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public bool IsTransfer { get; set; }
    public Guid? TransferGroupId { get; set; }
    public Guid ImportBatchId { get; set; }
    public string SourceRowHash { get; set; } = string.Empty;
    public CategorizationSource CategorizationSource { get; set; } = CategorizationSource.Manual;
    public bool NeedsReview { get; set; }
    public decimal? LlmConfidence { get; set; }
    public string? LlmModel { get; set; }
    public Guid? AppliedRuleId { get; set; }
    public DateTime? CategorizedUtc { get; set; }
}
