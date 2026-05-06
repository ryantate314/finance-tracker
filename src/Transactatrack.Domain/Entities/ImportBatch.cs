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
    public LlmCategorizationStatus LlmStatus { get; set; } = LlmCategorizationStatus.None;
    public int LlmRowsTotal { get; set; }
    public int LlmRowsDone { get; set; }
}
