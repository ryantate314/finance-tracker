using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transactatrack.Application;
using Transactatrack.Application.Categorization;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Infrastructure.Categorization;

public class CategorizationService : ICategorizationService
{
    private static readonly int LlmBatchSize = 5;

    private readonly AppDbContext _db;
    private readonly IRuleEngine _ruleEngine;
    private readonly IOllamaCategorizer _ollama;
    private readonly IFamilyContext _familyContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CategorizationService> _logger;

    public CategorizationService(
        AppDbContext db,
        IRuleEngine ruleEngine,
        IOllamaCategorizer ollama,
        IFamilyContext familyContext,
        IServiceScopeFactory scopeFactory,
        ILogger<CategorizationService> logger)
    {
        _db = db;
        _ruleEngine = ruleEngine;
        _ollama = ollama;
        _familyContext = familyContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ApplyRulesAsync(IReadOnlyList<Transaction> transactions, CancellationToken ct)
    {
        if (transactions.Count == 0) return;

        var familyId = _familyContext.ActiveFamilyId;
        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();

        var rules = await _db.CategoryRules
            .Where(r => r.IsEnabled && (r.Scope == RuleScope.Family || accountIds.Contains(r.AccountId!.Value)))
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var tx in transactions)
        {
            // Don't overwrite manual categorizations; don't re-apply on already-categorized rows at upload time
            if (tx.CategorizationSource == CategorizationSource.Manual && tx.CategoryId != null)
                continue;

            var result = _ruleEngine.Evaluate(tx, rules);
            if (result is null) continue;

            tx.CategoryId = result.Value.CategoryId;
            tx.SubCategoryId = result.Value.SubCategoryId;
            tx.CategorizationSource = CategorizationSource.Rule;
            tx.AppliedRuleId = result.Value.RuleId;
            tx.NeedsReview = false;
            tx.CategorizedUtc = now;
        }
    }

    public async Task StartLlmAsync(Guid batchId, CancellationToken ct)
    {
        var familyId = _familyContext.ActiveFamilyId;

        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new InvalidOperationException($"Batch {batchId} not found.");

        var uncategorized = await _db.Transactions
            .Where(t => t.ImportBatchId == batchId && t.CategoryId == null)
            .ToListAsync(ct);

        batch.LlmStatus = LlmCategorizationStatus.Running;
        batch.LlmRowsTotal = uncategorized.Count;
        batch.LlmRowsDone = 0;
        await _db.SaveChangesAsync(ct);

        if (uncategorized.Count == 0)
        {
            batch.LlmStatus = LlmCategorizationStatus.Complete;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Fire and forget — use a fresh scope so the background work outlives this request.
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var familyCtx = scope.ServiceProvider.GetRequiredService<FamilyContext>();
            familyCtx.ActiveFamilyId = familyId;

            var ollama = scope.ServiceProvider.GetRequiredService<IOllamaCategorizer>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CategorizationService>>();

            try
            {
                var categories = await db.Categories.ToListAsync();
                var subCategories = await db.SubCategories.ToListAsync();
                var now = DateTime.UtcNow;

                for (var i = 0; i < uncategorized.Count; i += LlmBatchSize)
                {
                    var batchRows = uncategorized.Skip(i).Take(LlmBatchSize).ToList();

                    var suggestions = await ollama.SuggestAsync(batchRows, categories, subCategories, CancellationToken.None);

                    foreach (var tx in batchRows)
                    {
                        if (!suggestions.TryGetValue(tx.Id, out var suggestion)) continue;

                        var dbTx = await db.Transactions.FindAsync(tx.Id);
                        if (dbTx is null || dbTx.CategorizationSource == CategorizationSource.Manual) continue;

                        dbTx.CategoryId = suggestion.CategoryId;
                        dbTx.SubCategoryId = suggestion.SubCategoryId;
                        dbTx.CategorizationSource = CategorizationSource.Llm;
                        dbTx.LlmConfidence = suggestion.Confidence;
                        dbTx.LlmModel = suggestion.Model;
                        dbTx.NeedsReview = true;
                        dbTx.CategorizedUtc = now;
                    }

                    var batchEntity = await db.ImportBatches.FindAsync(batchId);
                    if (batchEntity != null)
                    {
                        batchEntity.LlmRowsDone = Math.Min(i + LlmBatchSize, uncategorized.Count);
                        await db.SaveChangesAsync();
                    }
                }

                var finalBatch = await db.ImportBatches.FindAsync(batchId);
                if (finalBatch != null)
                {
                    finalBatch.LlmStatus = LlmCategorizationStatus.Complete;
                    finalBatch.LlmRowsDone = uncategorized.Count;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LLM categorization failed for batch {BatchId}", batchId);
                var failBatch = await db.ImportBatches.FindAsync(batchId);
                if (failBatch != null)
                {
                    failBatch.LlmStatus = LlmCategorizationStatus.Failed;
                    await db.SaveChangesAsync();
                }
            }
        });
    }

    public async Task RerunRulesAsync(Guid batchId, CancellationToken ct)
    {
        var transactions = await _db.Transactions
            .Where(t => t.ImportBatchId == batchId)
            .ToListAsync(ct);

        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();

        var rules = await _db.CategoryRules
            .Where(r => r.IsEnabled && (r.Scope == RuleScope.Family || accountIds.Contains(r.AccountId!.Value)))
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var tx in transactions)
        {
            // Preserve explicitly manual categorizations; uncategorized rows (Manual + null CategoryId) may be re-evaluated.
            if (tx.CategorizationSource == CategorizationSource.Manual && tx.CategoryId != null) continue;

            var result = _ruleEngine.Evaluate(tx, rules);
            if (result is null) continue;

            tx.CategoryId = result.Value.CategoryId;
            tx.SubCategoryId = result.Value.SubCategoryId;
            tx.CategorizationSource = CategorizationSource.Rule;
            tx.AppliedRuleId = result.Value.RuleId;
            tx.NeedsReview = false;
            tx.CategorizedUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}
