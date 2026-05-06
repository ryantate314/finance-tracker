using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.CategoryRules;

public record CreateCategoryRuleRequest(
    int Priority,
    RuleMatchField MatchField,
    RuleMatchType MatchType,
    string Pattern,
    decimal? AmountMin,
    decimal? AmountMax,
    Guid TargetCategoryId,
    RuleScope Scope,
    Guid? AccountId,
    bool IsEnabled
);
