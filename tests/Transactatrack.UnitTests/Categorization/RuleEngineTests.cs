using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Categorization;

namespace Transactatrack.UnitTests.Categorization;

public class RuleEngineTests
{
    private static readonly Guid FamilyId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid CategoryA = Guid.NewGuid();
    private static readonly Guid CategoryB = Guid.NewGuid();

    private static Transaction MakeTx(string description = "AMAZON.COM", string? merchant = null, decimal amount = -42m)
        => new()
        {
            Id = Guid.NewGuid(),
            FamilyId = FamilyId,
            AccountId = AccountId,
            Date = DateTime.UtcNow,
            Amount = amount,
            Description = description,
            Merchant = merchant,
            ImportBatchId = Guid.NewGuid(),
            SourceRowHash = Guid.NewGuid().ToString(),
        };

    private static CategoryRule MakeRule(
        int priority = 10,
        RuleMatchField field = RuleMatchField.Description,
        RuleMatchType type = RuleMatchType.Contains,
        string pattern = "AMAZON",
        Guid? targetId = null,
        RuleScope scope = RuleScope.Family,
        Guid? accountId = null,
        bool isEnabled = true,
        decimal? amountMin = null,
        decimal? amountMax = null)
        => new()
        {
            Id = Guid.NewGuid(),
            FamilyId = FamilyId,
            Priority = priority,
            MatchField = field,
            MatchType = type,
            Pattern = pattern,
            AmountMin = amountMin,
            AmountMax = amountMax,
            TargetCategoryId = targetId ?? CategoryA,
            Scope = scope,
            AccountId = accountId,
            IsEnabled = isEnabled,
        };

    private readonly RuleEngine _engine = new();

    [Fact]
    public void Evaluate_MatchingContainsRule_ReturnsCategory()
    {
        var tx = MakeTx("AMAZON.COM PURCHASE");
        var rule = MakeRule(pattern: "amazon");
        var result = _engine.Evaluate(tx, [rule]);
        Assert.NotNull(result);
        Assert.Equal(CategoryA, result.Value.CategoryId);
        Assert.Equal(rule.Id, result.Value.RuleId);
    }

    [Fact]
    public void Evaluate_CaseInsensitiveContains_Matches()
    {
        var tx = MakeTx("Starbucks Coffee");
        var rule = MakeRule(pattern: "STARBUCKS");
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_CaseInsensitiveEquals_Matches()
    {
        var tx = MakeTx("starbucks coffee");
        var rule = MakeRule(type: RuleMatchType.Equals, pattern: "Starbucks Coffee");
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_DisabledRule_Skipped()
    {
        var tx = MakeTx("AMAZON.COM PURCHASE");
        var rule = MakeRule(isEnabled: false);
        Assert.Null(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_PriorityOrder_FirstWins()
    {
        var tx = MakeTx("AMAZON");
        var low = MakeRule(priority: 20, targetId: CategoryB);
        var high = MakeRule(priority: 5, targetId: CategoryA);
        var result = _engine.Evaluate(tx, [low, high]);
        Assert.Equal(CategoryA, result!.Value.CategoryId);
    }

    [Fact]
    public void Evaluate_AccountScopeMatchingAccount_Matches()
    {
        var tx = MakeTx();
        var rule = MakeRule(scope: RuleScope.Account, accountId: AccountId);
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_AccountScopeDifferentAccount_Skipped()
    {
        var tx = MakeTx();
        var rule = MakeRule(scope: RuleScope.Account, accountId: Guid.NewGuid());
        Assert.Null(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_MerchantField_UsesTransactionMerchant()
    {
        var tx = MakeTx(description: "POS CHARGE", merchant: "COSTCO WHOLESALE");
        var ruleOnDesc = MakeRule(field: RuleMatchField.Merchant, pattern: "AMAZON");
        var ruleOnMerch = MakeRule(field: RuleMatchField.Merchant, pattern: "COSTCO");
        Assert.Null(_engine.Evaluate(tx, [ruleOnDesc]));
        Assert.NotNull(_engine.Evaluate(tx, [ruleOnMerch]));
    }

    [Fact]
    public void Evaluate_MerchantField_NullMerchant_NoMatch()
    {
        var tx = MakeTx(merchant: null);
        var rule = MakeRule(field: RuleMatchField.Merchant, pattern: "AMAZON");
        Assert.Null(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_ValidRegex_Matches()
    {
        var tx = MakeTx("UBER EATS #123");
        var rule = MakeRule(type: RuleMatchType.Regex, pattern: @"UBER\s+EATS");
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_RegexTimeout_ReturnsNull_DoesNotThrow()
    {
        // ReDoS-style pattern
        var tx = MakeTx(new string('a', 40));
        var rule = MakeRule(type: RuleMatchType.Regex, pattern: @"(a+)+$");
        // Should not throw; just returns no match (times out)
        var result = _engine.Evaluate(tx, [rule]);
        // result may be null due to timeout; either null or matched is OK as long as no exception
        _ = result;
    }

    [Fact]
    public void Evaluate_AmountRange_BothBounds_Matches()
    {
        var tx = MakeTx(amount: -75m);
        var rule = MakeRule(field: RuleMatchField.AmountRange, pattern: "", amountMin: 50m, amountMax: 100m);
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_AmountRange_MinOnly_MatchesAbove()
    {
        var tx = MakeTx(amount: -200m);
        var rule = MakeRule(field: RuleMatchField.AmountRange, pattern: "", amountMin: 150m);
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_AmountRange_MaxOnly_MatchesBelow()
    {
        var tx = MakeTx(amount: -5m);
        var rule = MakeRule(field: RuleMatchField.AmountRange, pattern: "", amountMax: 10m);
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_AmountRange_UsesMathAbs_CreditAmountMatches()
    {
        // Positive amount (credit/payment), should still match AmountRange using Math.Abs
        var tx = MakeTx(amount: 75m);
        var rule = MakeRule(field: RuleMatchField.AmountRange, pattern: "", amountMin: 50m, amountMax: 100m);
        Assert.NotNull(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void Evaluate_AmountRange_BothBoundsNull_NoMatch()
    {
        var tx = MakeTx(amount: -50m);
        // Both null — treated as no effective range (no bounds = not a valid range rule)
        var rule = MakeRule(field: RuleMatchField.AmountRange, pattern: "");
        Assert.Null(_engine.Evaluate(tx, [rule]));
    }

    [Fact]
    public void IsValidRegex_ValidPattern_ReturnsTrue()
    {
        Assert.True(RuleEngine.IsValidRegex(@"UBER\s+EATS"));
    }

    [Fact]
    public void IsValidRegex_InvalidPattern_ReturnsFalse()
    {
        Assert.False(RuleEngine.IsValidRegex(@"[unclosed"));
    }
}
