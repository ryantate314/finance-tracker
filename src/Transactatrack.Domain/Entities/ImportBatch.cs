using Transactatrack.Domain.Common;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Domain.Entities;

public class ImportBatch : FamilyScopedEntity
{
    public Guid AccountId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public DateTime UploadedUtc { get; set; }
    public ImportBatchStatus Status { get; set; }
}
