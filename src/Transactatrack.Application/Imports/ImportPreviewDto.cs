namespace Transactatrack.Application.Imports;

public record ImportPreviewDto(
    Guid BatchId,
    Guid AccountId,
    string BankCode,
    string OriginalFilename,
    DateTime UploadedUtc,
    int TotalRows,
    int NewCount,
    int DuplicateCount,
    IReadOnlyList<ImportPreviewRowDto> Sample
);

public record ImportPreviewRowDto(
    DateTime Date,
    DateTime? PostedDate,
    decimal Amount,
    string Description,
    bool IsDuplicate
);
