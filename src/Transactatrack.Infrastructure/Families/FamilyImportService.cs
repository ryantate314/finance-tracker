using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Families;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Infrastructure.Families;

public class FamilyImportService : IFamilyImportService
{
    private readonly AppDbContext _db;

    public FamilyImportService(AppDbContext db) => _db = db;

    public async Task<FamilyImportSummaryDto> ImportAsNewAsync(
        FamilyExportDto export, string? nameOverride, CancellationToken ct)
    {
        Guid newFamilyId = Guid.NewGuid();
        string familyName = string.IsNullOrWhiteSpace(nameOverride) ? export.Family.Name : nameOverride!;

        // Generate fresh GUIDs for every entity. Preserving source GUIDs would collide
        // on global PKs whenever the target DB has ever seen any of them (e.g.,
        // round-tripping into the same database).
        var ownerMap = export.Owners.ToDictionary(o => o.Id, _ => Guid.NewGuid());
        var categoryMap = export.Categories.ToDictionary(c => c.Id, _ => Guid.NewGuid());
        var subCategoryMap = export.Categories
            .SelectMany(c => c.SubCategories)
            .ToDictionary(s => s.Id, _ => Guid.NewGuid());
        var accountMap = export.Accounts.ToDictionary(a => a.Id, _ => Guid.NewGuid());
        var ruleMap = export.CategoryRules.ToDictionary(r => r.Id, _ => Guid.NewGuid());
        var batchMap = export.ImportBatches.ToDictionary(b => b.Id, _ => Guid.NewGuid());
        var transferGroupMap = new Dictionary<Guid, Guid>();

        Guid? RemapNullable(Guid? id, IReadOnlyDictionary<Guid, Guid> map) =>
            id.HasValue && map.TryGetValue(id.Value, out var v) ? v : null;

        _db.SuppressAutoStamping = true;
        try
        {
            await using var dbTx = await _db.Database.BeginTransactionAsync(ct);

            _db.Families.Add(new Family
            {
                Id = newFamilyId,
                Name = familyName,
                CreatedUtc = DateTime.UtcNow,
            });

            foreach (var o in export.Owners)
            {
                _db.Owners.Add(new Owner
                {
                    Id = ownerMap[o.Id],
                    FamilyId = newFamilyId,
                    CreatedUtc = o.CreatedUtc,
                    Name = o.Name,
                });
            }

            foreach (var c in export.Categories)
            {
                _db.Categories.Add(new Category
                {
                    Id = categoryMap[c.Id],
                    FamilyId = newFamilyId,
                    CreatedUtc = c.CreatedUtc,
                    Name = c.Name,
                    Kind = c.Kind,
                });
            }

            int subCategoryCount = 0;
            foreach (var c in export.Categories)
            {
                foreach (var s in c.SubCategories)
                {
                    _db.SubCategories.Add(new SubCategory
                    {
                        Id = subCategoryMap[s.Id],
                        FamilyId = newFamilyId,
                        CreatedUtc = s.CreatedUtc,
                        CategoryId = categoryMap[s.CategoryId],
                        Name = s.Name,
                    });
                    subCategoryCount++;
                }
            }

            foreach (var a in export.Accounts)
            {
                _db.Accounts.Add(new Account
                {
                    Id = accountMap[a.Id],
                    FamilyId = newFamilyId,
                    CreatedUtc = a.CreatedUtc,
                    OwnerId = ownerMap[a.OwnerId],
                    Name = a.Name,
                    Institution = a.Institution,
                    AccountType = a.AccountType,
                    BankCode = a.BankCode,
                });
            }

            foreach (var r in export.CategoryRules)
            {
                _db.CategoryRules.Add(new CategoryRule
                {
                    Id = ruleMap[r.Id],
                    FamilyId = newFamilyId,
                    CreatedUtc = DateTime.UtcNow,
                    Priority = r.Priority,
                    MatchField = r.MatchField,
                    MatchType = r.MatchType,
                    Pattern = r.Pattern,
                    AmountMin = r.AmountMin,
                    AmountMax = r.AmountMax,
                    TargetCategoryId = categoryMap[r.TargetCategoryId],
                    TargetSubCategoryId = RemapNullable(r.TargetSubCategoryId, subCategoryMap),
                    Scope = r.Scope,
                    AccountId = RemapNullable(r.AccountId, accountMap),
                    IsEnabled = r.IsEnabled,
                });
            }

            foreach (var b in export.ImportBatches)
            {
                _db.ImportBatches.Add(new ImportBatch
                {
                    Id = batchMap[b.Id],
                    FamilyId = newFamilyId,
                    CreatedUtc = DateTime.UtcNow,
                    AccountId = accountMap[b.AccountId],
                    BankCode = b.BankCode,
                    OriginalFilename = b.OriginalFilename,
                    UploadedUtc = b.UploadedUtc,
                    Status = b.Status,
                    LlmStatus = b.LlmStatus,
                    LlmRowsTotal = b.LlmRowsTotal,
                    LlmRowsDone = b.LlmRowsDone,
                });
            }

            foreach (var t in export.Transactions)
            {
                Guid? transferGroupId = null;
                if (t.TransferGroupId.HasValue)
                {
                    if (!transferGroupMap.TryGetValue(t.TransferGroupId.Value, out var mappedGroup))
                    {
                        mappedGroup = Guid.NewGuid();
                        transferGroupMap[t.TransferGroupId.Value] = mappedGroup;
                    }
                    transferGroupId = mappedGroup;
                }

                _db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    FamilyId = newFamilyId,
                    CreatedUtc = t.CreatedUtc,
                    AccountId = accountMap[t.AccountId],
                    Date = t.Date,
                    PostedDate = t.PostedDate,
                    Amount = t.Amount,
                    Description = t.Description,
                    Merchant = t.Merchant,
                    Note = t.Note,
                    CategoryId = RemapNullable(t.CategoryId, categoryMap),
                    SubCategoryId = RemapNullable(t.SubCategoryId, subCategoryMap),
                    IsTransfer = t.IsTransfer,
                    TransferGroupId = transferGroupId,
                    ImportBatchId = batchMap[t.ImportBatchId],
                    SourceRowHash = t.SourceRowHash,
                    CategorizationSource = t.CategorizationSource,
                    NeedsReview = t.NeedsReview,
                    LlmConfidence = t.LlmConfidence,
                    LlmModel = t.LlmModel,
                    AppliedRuleId = RemapNullable(t.AppliedRuleId, ruleMap),
                    CategorizedUtc = t.CategorizedUtc,
                });
            }

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            return new FamilyImportSummaryDto(
                newFamilyId, familyName,
                OwnersInserted: export.Owners.Count, OwnersSkipped: 0,
                AccountsInserted: export.Accounts.Count, AccountsSkipped: 0,
                CategoriesInserted: export.Categories.Count, CategoriesSkipped: 0, CategoriesRemapped: 0,
                SubCategoriesInserted: subCategoryCount, SubCategoriesSkipped: 0,
                CategoryRulesInserted: export.CategoryRules.Count, CategoryRulesSkipped: 0,
                ImportBatchesInserted: export.ImportBatches.Count, ImportBatchesSkipped: 0,
                TransactionsInserted: export.Transactions.Count, TransactionsSkipped: 0);
        }
        finally
        {
            _db.SuppressAutoStamping = false;
        }
    }

    public async Task<FamilyImportSummaryDto?> MergeAsync(
        Guid targetFamilyId, FamilyExportDto export, CancellationToken ct)
    {
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == targetFamilyId, ct);
        if (family is null) return null;

        _db.SuppressAutoStamping = true;
        try
        {
            await using var dbTx = await _db.Database.BeginTransactionAsync(ct);

            // Pre-load existing target Ids for skip checks.
            var existingOwnerIds = await Set<Owner>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingAccountIds = await Set<Account>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingCategoryIds = await Set<Category>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingSubCategoryIds = await Set<SubCategory>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingRuleIds = await Set<CategoryRule>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingBatchIds = await Set<ImportBatch>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingTxIds = await Set<Transaction>(targetFamilyId).Select(e => e.Id).ToHashSetAsync(ct);
            var existingTxHashRows = await Set<Transaction>(targetFamilyId)
                .Select(e => new { e.AccountId, e.SourceRowHash })
                .ToListAsync(ct);
            var existingTxHashSet = existingTxHashRows
                .Select(x => (x.AccountId, x.SourceRowHash))
                .ToHashSet();

            var targetSystemCategoryByKind = await Set<Category>(targetFamilyId)
                .Where(c => c.Kind != CategoryKind.User)
                .ToDictionaryAsync(c => c.Kind, c => c.Id, ct);

            // Owners
            int ownersInserted = 0, ownersSkipped = 0;
            foreach (var o in export.Owners)
            {
                if (existingOwnerIds.Contains(o.Id)) { ownersSkipped++; continue; }
                _db.Owners.Add(new Owner
                {
                    Id = o.Id, FamilyId = targetFamilyId, CreatedUtc = o.CreatedUtc, Name = o.Name,
                });
                existingOwnerIds.Add(o.Id);
                ownersInserted++;
            }

            // Categories — remap non-User Kinds to target's existing same-Kind category.
            var categoryRemap = new Dictionary<Guid, Guid>();
            int categoriesInserted = 0, categoriesSkipped = 0, categoriesRemapped = 0;
            foreach (var c in export.Categories)
            {
                if (c.Kind != CategoryKind.User &&
                    targetSystemCategoryByKind.TryGetValue(c.Kind, out var existingSystemId))
                {
                    if (c.Id != existingSystemId)
                        categoryRemap[c.Id] = existingSystemId;
                    categoriesRemapped++;
                    continue;
                }
                if (existingCategoryIds.Contains(c.Id)) { categoriesSkipped++; continue; }
                _db.Categories.Add(new Category
                {
                    Id = c.Id, FamilyId = targetFamilyId, CreatedUtc = c.CreatedUtc,
                    Name = c.Name, Kind = c.Kind,
                });
                existingCategoryIds.Add(c.Id);
                categoriesInserted++;
            }

            Guid Remap(Guid id) => categoryRemap.TryGetValue(id, out var v) ? v : id;

            // SubCategories — flattened from each Category in the export.
            int subCategoriesInserted = 0, subCategoriesSkipped = 0;
            foreach (var c in export.Categories)
            {
                foreach (var s in c.SubCategories)
                {
                    if (existingSubCategoryIds.Contains(s.Id)) { subCategoriesSkipped++; continue; }
                    _db.SubCategories.Add(new SubCategory
                    {
                        Id = s.Id, FamilyId = targetFamilyId, CreatedUtc = s.CreatedUtc,
                        CategoryId = Remap(s.CategoryId), Name = s.Name,
                    });
                    existingSubCategoryIds.Add(s.Id);
                    subCategoriesInserted++;
                }
            }

            // Accounts
            int accountsInserted = 0, accountsSkipped = 0;
            foreach (var a in export.Accounts)
            {
                if (existingAccountIds.Contains(a.Id)) { accountsSkipped++; continue; }
                _db.Accounts.Add(new Account
                {
                    Id = a.Id, FamilyId = targetFamilyId, CreatedUtc = a.CreatedUtc,
                    OwnerId = a.OwnerId, Name = a.Name, Institution = a.Institution,
                    AccountType = a.AccountType, BankCode = a.BankCode,
                });
                existingAccountIds.Add(a.Id);
                accountsInserted++;
            }

            // CategoryRules
            int rulesInserted = 0, rulesSkipped = 0;
            foreach (var r in export.CategoryRules)
            {
                if (existingRuleIds.Contains(r.Id)) { rulesSkipped++; continue; }
                _db.CategoryRules.Add(new CategoryRule
                {
                    Id = r.Id, FamilyId = targetFamilyId, CreatedUtc = DateTime.UtcNow,
                    Priority = r.Priority, MatchField = r.MatchField, MatchType = r.MatchType,
                    Pattern = r.Pattern, AmountMin = r.AmountMin, AmountMax = r.AmountMax,
                    TargetCategoryId = Remap(r.TargetCategoryId),
                    TargetSubCategoryId = r.TargetSubCategoryId,
                    Scope = r.Scope, AccountId = r.AccountId, IsEnabled = r.IsEnabled,
                });
                existingRuleIds.Add(r.Id);
                rulesInserted++;
            }

            // ImportBatches
            int batchesInserted = 0, batchesSkipped = 0;
            foreach (var b in export.ImportBatches)
            {
                if (existingBatchIds.Contains(b.Id)) { batchesSkipped++; continue; }
                _db.ImportBatches.Add(new ImportBatch
                {
                    Id = b.Id, FamilyId = targetFamilyId, CreatedUtc = DateTime.UtcNow,
                    AccountId = b.AccountId, BankCode = b.BankCode,
                    OriginalFilename = b.OriginalFilename, UploadedUtc = b.UploadedUtc,
                    Status = b.Status, LlmStatus = b.LlmStatus,
                    LlmRowsTotal = b.LlmRowsTotal, LlmRowsDone = b.LlmRowsDone,
                });
                existingBatchIds.Add(b.Id);
                batchesInserted++;
            }

            // Transactions — skip on Id collision OR on (AccountId, SourceRowHash) collision.
            int txInserted = 0, txSkipped = 0;
            foreach (var t in export.Transactions)
            {
                if (existingTxIds.Contains(t.Id)) { txSkipped++; continue; }
                if (!string.IsNullOrEmpty(t.SourceRowHash) &&
                    existingTxHashSet.Contains((t.AccountId, t.SourceRowHash)))
                {
                    txSkipped++;
                    continue;
                }
                var remappedCategoryId = t.CategoryId.HasValue ? Remap(t.CategoryId.Value) : (Guid?)null;
                _db.Transactions.Add(BuildTransaction(t, targetFamilyId, remappedCategoryId));
                existingTxIds.Add(t.Id);
                existingTxHashSet.Add((t.AccountId, t.SourceRowHash));
                txInserted++;
            }

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            return new FamilyImportSummaryDto(
                targetFamilyId, family.Name,
                ownersInserted, ownersSkipped,
                accountsInserted, accountsSkipped,
                categoriesInserted, categoriesSkipped, categoriesRemapped,
                subCategoriesInserted, subCategoriesSkipped,
                rulesInserted, rulesSkipped,
                batchesInserted, batchesSkipped,
                txInserted, txSkipped);
        }
        finally
        {
            _db.SuppressAutoStamping = false;
        }
    }

    private IQueryable<T> Set<T>(Guid familyId) where T : Domain.Common.FamilyScopedEntity =>
        _db.Set<T>().IgnoreQueryFilters().Where(e => e.FamilyId == familyId);

    private static Transaction BuildTransaction(
        Application.Transactions.TransactionDto t, Guid familyId, Guid? categoryId) =>
        new()
        {
            Id = t.Id,
            FamilyId = familyId,
            CreatedUtc = t.CreatedUtc,
            AccountId = t.AccountId,
            Date = t.Date,
            PostedDate = t.PostedDate,
            Amount = t.Amount,
            Description = t.Description,
            Merchant = t.Merchant,
            Note = t.Note,
            CategoryId = categoryId,
            SubCategoryId = t.SubCategoryId,
            IsTransfer = t.IsTransfer,
            TransferGroupId = t.TransferGroupId,
            ImportBatchId = t.ImportBatchId,
            SourceRowHash = t.SourceRowHash,
            CategorizationSource = t.CategorizationSource,
            NeedsReview = t.NeedsReview,
            LlmConfidence = t.LlmConfidence,
            LlmModel = t.LlmModel,
            AppliedRuleId = t.AppliedRuleId,
            CategorizedUtc = t.CategorizedUtc,
        };
}
