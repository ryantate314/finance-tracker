using Transactatrack.Domain.Entities;

namespace Transactatrack.Application.Categorization;

public record LlmCategorizationResult(Guid CategoryId, Guid? SubCategoryId, decimal Confidence, string Model);

public interface IOllamaCategorizer
{
    Task<IDictionary<Guid, LlmCategorizationResult>> SuggestAsync(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCategory> subCategories,
        CancellationToken ct);
}
