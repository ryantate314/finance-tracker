namespace Transactatrack.Application.Families;

public interface IFamilyImportService
{
    Task<FamilyImportSummaryDto> ImportAsNewAsync(FamilyExportDto export, string? nameOverride, CancellationToken ct);
    Task<FamilyImportSummaryDto?> MergeAsync(Guid targetFamilyId, FamilyExportDto export, CancellationToken ct);
}
