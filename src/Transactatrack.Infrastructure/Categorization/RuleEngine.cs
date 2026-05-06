using System.Text.RegularExpressions;
using Transactatrack.Application.Categorization;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Infrastructure.Categorization;

public class RuleEngine : IRuleEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public (Guid CategoryId, Guid? SubCategoryId, Guid RuleId)? Evaluate(Transaction tx, IReadOnlyList<CategoryRule> rules)
    {
        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (rule.Scope == RuleScope.Account && rule.AccountId != tx.AccountId)
                continue;

            if (Matches(tx, rule))
                return (rule.TargetCategoryId, rule.TargetSubCategoryId, rule.Id);
        }
        return null;
    }

    private static bool Matches(Transaction tx, CategoryRule rule)
    {
        return rule.MatchField switch
        {
            RuleMatchField.Description => MatchesText(tx.Description, rule),
            RuleMatchField.Merchant => tx.Merchant is not null && MatchesText(tx.Merchant, rule),
            RuleMatchField.AmountRange => MatchesAmountRange(tx.Amount, rule),
            _ => false
        };
    }

    private static bool MatchesText(string value, CategoryRule rule)
    {
        return rule.MatchType switch
        {
            RuleMatchType.Contains => value.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Equals => string.Equals(value, rule.Pattern, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Regex => MatchesRegex(value, rule.Pattern),
            _ => false
        };
    }

    private static bool MatchesRegex(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool MatchesAmountRange(decimal amount, CategoryRule rule)
    {
        var abs = Math.Abs(amount);
        if (rule.AmountMin.HasValue && abs < rule.AmountMin.Value)
            return false;
        if (rule.AmountMax.HasValue && abs > rule.AmountMax.Value)
            return false;
        return rule.AmountMin.HasValue || rule.AmountMax.HasValue;
    }

    public static bool IsValidRegex(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
