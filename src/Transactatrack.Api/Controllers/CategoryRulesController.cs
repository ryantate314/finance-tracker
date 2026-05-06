using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.CategoryRules;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Categorization;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/category-rules")]
public class CategoryRulesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoryRulesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryRuleDto>>> List(CancellationToken ct)
    {
        var rules = await _db.CategoryRules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .Select(r => ToDto(r))
            .ToListAsync(ct);
        return Ok(rules);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryRuleDto>> Create(CreateCategoryRuleRequest request, CancellationToken ct)
    {
        if (ValidationError(request.MatchField, request.MatchType, request.Pattern,
            request.AmountMin, request.AmountMax, request.Scope, request.AccountId) is { } err)
            return BadRequest(new { title = err, status = 400 });

        if (await SubCategoryParentError(request.TargetCategoryId, request.TargetSubCategoryId, ct) is { } subErr)
            return BadRequest(new { title = subErr, status = 400 });

        var rule = new CategoryRule
        {
            Priority = request.Priority,
            MatchField = request.MatchField,
            MatchType = request.MatchType,
            Pattern = request.Pattern,
            AmountMin = request.AmountMin,
            AmountMax = request.AmountMax,
            TargetCategoryId = request.TargetCategoryId,
            TargetSubCategoryId = request.TargetSubCategoryId,
            Scope = request.Scope,
            AccountId = request.AccountId,
            IsEnabled = request.IsEnabled,
        };
        _db.CategoryRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = rule.Id }, ToDto(rule));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryRuleDto>> Get(Guid id, CancellationToken ct)
    {
        var rule = await _db.CategoryRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        return Ok(ToDto(rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCategoryRuleRequest request, CancellationToken ct)
    {
        var rule = await _db.CategoryRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        if (ValidationError(request.MatchField, request.MatchType, request.Pattern,
            request.AmountMin, request.AmountMax, request.Scope, request.AccountId) is { } err)
            return BadRequest(new { title = err, status = 400 });

        if (await SubCategoryParentError(request.TargetCategoryId, request.TargetSubCategoryId, ct) is { } subErr)
            return BadRequest(new { title = subErr, status = 400 });

        rule.Priority = request.Priority;
        rule.MatchField = request.MatchField;
        rule.MatchType = request.MatchType;
        rule.Pattern = request.Pattern;
        rule.AmountMin = request.AmountMin;
        rule.AmountMax = request.AmountMax;
        rule.TargetCategoryId = request.TargetCategoryId;
        rule.TargetSubCategoryId = request.TargetSubCategoryId;
        rule.Scope = request.Scope;
        rule.AccountId = request.AccountId;
        rule.IsEnabled = request.IsEnabled;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var rule = await _db.CategoryRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        _db.CategoryRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("order")]
    public async Task<IActionResult> Reorder(IEnumerable<RuleOrderUpdate> updates, CancellationToken ct)
    {
        var updateList = updates.ToList();
        var ids = updateList.Select(u => u.Id).ToList();
        var rules = await _db.CategoryRules.Where(r => ids.Contains(r.Id)).ToListAsync(ct);

        foreach (var rule in rules)
        {
            var update = updateList.FirstOrDefault(u => u.Id == rule.Id);
            if (update is not null) rule.Priority = update.Priority;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? ValidationError(
        RuleMatchField matchField, RuleMatchType matchType, string pattern,
        decimal? amountMin, decimal? amountMax, RuleScope scope, Guid? accountId)
    {
        if (scope == RuleScope.Account && accountId is null)
            return "AccountId is required when Scope is Account.";

        if (matchField == RuleMatchField.AmountRange)
        {
            if (amountMin is null && amountMax is null)
                return "At least one of AmountMin or AmountMax must be set for AmountRange rules.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "Pattern is required for Description and Merchant rules.";

            if (matchType == RuleMatchType.Regex && !RuleEngine.IsValidRegex(pattern))
                return $"Pattern '{pattern}' is not a valid regular expression.";
        }

        return null;
    }

    private async Task<string?> SubCategoryParentError(Guid categoryId, Guid? subCategoryId, CancellationToken ct)
    {
        if (subCategoryId is null) return null;
        var belongs = await _db.SubCategories
            .AnyAsync(s => s.Id == subCategoryId.Value && s.CategoryId == categoryId, ct);
        return belongs ? null : "TargetSubCategoryId must belong to TargetCategoryId.";
    }

    private static CategoryRuleDto ToDto(CategoryRule r) => new(
        r.Id, r.Priority, r.MatchField, r.MatchType, r.Pattern,
        r.AmountMin, r.AmountMax, r.TargetCategoryId, r.TargetSubCategoryId, r.Scope, r.AccountId, r.IsEnabled);
}
