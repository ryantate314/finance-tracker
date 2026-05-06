using Transactatrack.Domain.Entities;

namespace Transactatrack.Application.Categorization;

public interface IRuleEngine
{
    (Guid CategoryId, Guid? SubCategoryId, Guid RuleId)? Evaluate(Transaction tx, IReadOnlyList<CategoryRule> rules);
}
