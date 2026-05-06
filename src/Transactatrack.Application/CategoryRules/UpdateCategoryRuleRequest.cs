using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.CategoryRules;

public record UpdateCategoryRuleRequest(
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
