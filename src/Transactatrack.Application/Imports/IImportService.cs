namespace Transactatrack.Application.Imports;

public interface IImportService
{
    Task<ImportPreviewDto> UploadAsync(Guid accountId, Stream csv, string filename, CancellationToken ct);
    Task CommitAsync(Guid batchId, CancellationToken ct);
    Task DiscardAsync(Guid batchId, CancellationToken ct);
}
