using Transactatrack.Domain.Common;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Domain.Entities;

public class CategoryRule : FamilyScopedEntity
{
    public int Priority { get; set; }
    public RuleMatchField MatchField { get; set; }
    public RuleMatchType MatchType { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public decimal? AmountMin { get; set; }
    public decimal? AmountMax { get; set; }
    public Guid TargetCategoryId { get; set; }
    public Guid? TargetSubCategoryId { get; set; }
    public RuleScope Scope { get; set; }
    public Guid? AccountId { get; set; }
    public bool IsEnabled { get; set; }
}
