using Transactatrack.Domain.Common;

namespace Transactatrack.Domain.Entities;

public class Transaction : FamilyScopedEntity
{
    public Guid AccountId { get; set; }
    public DateTime Date { get; set; }
    public DateTime? PostedDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsTransfer { get; set; }
    public Guid? TransferGroupId { get; set; }
    public Guid ImportBatchId { get; set; }
    public string SourceRowHash { get; set; } = string.Empty;
}
