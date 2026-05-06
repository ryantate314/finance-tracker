using Transactatrack.Domain.Entities;

namespace Transactatrack.Application.Categorization;

public interface ICategorizationService
{
    Task ApplyRulesAsync(IReadOnlyList<Transaction> transactions, CancellationToken ct);
    Task StartLlmAsync(Guid batchId, CancellationToken ct);
    Task RerunRulesAsync(Guid batchId, CancellationToken ct);
}
