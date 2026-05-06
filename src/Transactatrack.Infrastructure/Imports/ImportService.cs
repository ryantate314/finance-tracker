using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Categorization;
using Transactatrack.Application.Imports;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Infrastructure.Imports;

public class ImportService : IImportService
{
    private const int SamplePreviewSize = 50;

    private readonly AppDbContext _db;
    private readonly IBankParserRegistry _registry;
    private readonly SourceRowHasher _hasher;
    private readonly ICategorizationService _categorization;

    public ImportService(AppDbContext db, IBankParserRegistry registry, SourceRowHasher hasher, ICategorizationService categorization)
    {
        _db = db;
        _registry = registry;
        _hasher = hasher;
        _categorization = categorization;
    }

    public async Task<ImportPreviewDto> UploadAsync(Guid accountId, Stream csv, string filename, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new ImportException(404, $"Account {accountId} not found.");

        if (string.IsNullOrWhiteSpace(account.BankCode))
            throw new ImportException(400, "Account has no BankCode set.");

        var parser = _registry.Get(account.BankCode)
            ?? throw new ImportException(400, $"No parser registered for BankCode '{account.BankCode}'.");

        var pendingExists = await _db.ImportBatches
            .AnyAsync(b => b.AccountId == accountId && b.Status == ImportBatchStatus.Pending, ct);
        if (pendingExists)
            throw new ImportException(409, "A pending import already exists for this account. Commit or discard it first.");

        List<ParsedTransaction> parsed;
        try
        {
            parsed = parser.Parse(csv).ToList();
        }
        catch (ImportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ImportException(400, $"Failed to parse CSV: {ex.Message}");
        }

        // Trust the CSV: rows that hash the same represent distinct real transactions
        // (e.g. two $2 tips on the same day). Disambiguate within-batch collisions with
        // an occurrence suffix so the (AccountId, SourceRowHash) unique index holds.
        // Track base hash and ordinal per position for the backwards-compat dup check below.
        var occurrence = new Dictionary<string, int>();
        var hashes = new List<string>(parsed.Count);
        var baseHashes = new List<string>(parsed.Count);
        var ordinals = new List<int>(parsed.Count);
        foreach (var row in parsed)
        {
            var baseHash = _hasher.Hash(accountId, row.Date, row.Amount, row.Description);
            var ord = occurrence.GetValueOrDefault(baseHash, 0);
            occurrence[baseHash] = ord + 1;
            hashes.Add(ord == 0 ? baseHash : $"{baseHash}#{ord}");
            baseHashes.Add(baseHash);
            ordinals.Add(ord);
        }

        // IgnoreQueryFilters: the account itself was already family-scoped above (FirstOrDefaultAsync
        // ran with the active-family filter), so AccountId == accountId is equivalent to family scoping
        // here. Skipping the filter avoids an unnecessary join into the family scope on the hash lookup.
        var existing = await _db.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.AccountId == accountId && hashes.Contains(t.SourceRowHash))
            .Select(t => t.SourceRowHash)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var batch = new ImportBatch
        {
            AccountId = accountId,
            BankCode = account.BankCode,
            OriginalFilename = filename,
            UploadedUtc = DateTime.UtcNow,
            Status = ImportBatchStatus.Pending,
        };

        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        var newRows = new List<Transaction>();
        var duplicateRows = new List<ParsedTransaction>();

        for (var i = 0; i < parsed.Count; i++)
        {
            var hash = hashes[i];
            var row = parsed[i];

            // A suffixed row (ord > 0) is also a dup if its base hash exists in the DB.
            // This handles data imported before occurrence suffixes were introduced, where
            // only one occurrence was stored; on re-import the suffixed hash won't be found
            // by exact match but the base hash being present signals it was already imported.
            if (existingSet.Contains(hash) || (ordinals[i] > 0 && existingSet.Contains(baseHashes[i])))
            {
                duplicateRows.Add(row);
                continue;
            }

            newRows.Add(new Transaction
            {
                AccountId = accountId,
                Date = row.Date,
                PostedDate = row.PostedDate,
                Amount = row.Amount,
                Description = row.Description,
                Merchant = row.Merchant,
                ImportBatchId = batch.Id,
                SourceRowHash = hash,
            });
        }

        if (newRows.Count > 0)
        {
            _db.Transactions.AddRange(newRows);
            await _db.SaveChangesAsync(ct);

            // Apply categorization rules synchronously before returning the preview.
            await _categorization.ApplyRulesAsync(newRows, ct);
            await _db.SaveChangesAsync(ct);
        }

        await dbTx.CommitAsync(ct);

        // Sample includes both new and dropped rows so the user can see what was deduped.
        // Dropped rows are flagged IsDuplicate=true; new rows IsDuplicate=false.
        var sample = newRows
            .Take(SamplePreviewSize)
            .Select(t => new ImportPreviewRowDto(t.Date, t.PostedDate, t.Amount, t.Description, false, t.CategoryId, t.SubCategoryId, t.CategorizationSource, t.NeedsReview, t.Id))
            .Concat(duplicateRows
                .Take(SamplePreviewSize)
                .Select(r => new ImportPreviewRowDto(r.Date, r.PostedDate, r.Amount, r.Description, true)))
            .ToList();

        return new ImportPreviewDto(
            BatchId: batch.Id,
            AccountId: accountId,
            BankCode: batch.BankCode,
            OriginalFilename: batch.OriginalFilename,
            UploadedUtc: batch.UploadedUtc,
            TotalRows: parsed.Count,
            NewCount: newRows.Count,
            DuplicateCount: duplicateRows.Count,
            Sample: sample);
    }

    public async Task CommitAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new ImportException(404, $"Import batch {batchId} not found.");

        if (batch.Status != ImportBatchStatus.Pending)
            throw new ImportException(409, $"Batch is in status {batch.Status}; only Pending batches can be committed.");

        batch.Status = ImportBatchStatus.Committed;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DiscardAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new ImportException(404, $"Import batch {batchId} not found.");

        if (batch.Status != ImportBatchStatus.Pending)
            throw new ImportException(409, $"Batch is in status {batch.Status}; only Pending batches can be discarded.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Transactions.Where(t => t.ImportBatchId == batchId).ExecuteDeleteAsync(ct);
        _db.ImportBatches.Remove(batch);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new ImportException(404, $"Import batch {batchId} not found.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Transactions.Where(t => t.ImportBatchId == batchId).ExecuteDeleteAsync(ct);
        _db.ImportBatches.Remove(batch);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
