using Transactatrack.Domain.Entities;

namespace Transactatrack.Application.Categorization;

public record LlmCategorizationResult(Guid CategoryId, decimal Confidence, string Model);

public interface IOllamaCategorizer
{
    Task<IDictionary<Guid, LlmCategorizationResult>> SuggestAsync(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        CancellationToken ct);
}
