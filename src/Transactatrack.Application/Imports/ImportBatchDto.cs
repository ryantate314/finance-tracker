using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.Imports;

public record ImportBatchDto(
    Guid Id,
    Guid AccountId,
    string BankCode,
    string OriginalFilename,
    DateTime UploadedUtc,
    ImportBatchStatus Status,
    int TransactionCount,
    LlmCategorizationStatus LlmStatus = LlmCategorizationStatus.None,
    int LlmRowsTotal = 0,
    int LlmRowsDone = 0
);
