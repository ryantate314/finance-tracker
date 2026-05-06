using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.CategoryRules;

public record CategoryRuleDto(
    Guid Id,
    int Priority,
    RuleMatchField MatchField,
    RuleMatchType MatchType,
    string Pattern,
    decimal? AmountMin,
    decimal? AmountMax,
    Guid TargetCategoryId,
    Guid? TargetSubCategoryId,
    RuleScope Scope,
    Guid? AccountId,
    bool IsEnabled
);
