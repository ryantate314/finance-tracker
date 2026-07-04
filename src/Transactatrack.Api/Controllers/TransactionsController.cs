using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionDto>>> List(
        [FromQuery] string? accountIds,
        [FromQuery] string? categoryIds,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? q,
        [FromQuery] bool? needsReview,
        [FromQuery] bool? isTransfer,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        if (page < 1)
            return BadRequest(new { title = "page must be >= 1.", status = 400 });
        if (pageSize < 1)
            return BadRequest(new { title = "pageSize must be >= 1.", status = 400 });
        if (pageSize > MaxPageSize)
            return BadRequest(new { title = $"pageSize must be <= {MaxPageSize}.", status = 400 });

        var accountIdList = ParseGuidList(accountIds);
        var categoryIdList = ParseGuidList(categoryIds);

        var query =
            from t in _db.Transactions
            join b in _db.ImportBatches on t.ImportBatchId equals b.Id
            where b.Status == ImportBatchStatus.Committed
            select t;

        if (accountIdList.Count > 0)
            query = query.Where(t => accountIdList.Contains(t.AccountId));

        if (categoryIdList.Count > 0)
            query = query.Where(t => t.CategoryId != null && categoryIdList.Contains(t.CategoryId.Value));

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Date >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Date <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Escape ILIKE meta-chars so user input is treated as a literal substring.
            var escaped = q.Trim()
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
            var pattern = $"%{escaped}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Description, pattern, @"\") ||
                (t.Merchant != null && EF.Functions.ILike(t.Merchant, pattern, @"\")));
        }

        if (needsReview.HasValue)
            query = query.Where(t => t.NeedsReview == needsReview.Value);

        if (isTransfer.HasValue)
            query = query.Where(t => t.IsTransfer == isTransfer.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto(
                t.Id,
                t.AccountId,
                t.Date,
                t.PostedDate,
                t.Amount,
                t.Description,
                t.Merchant,
                t.CategoryId,
                t.SubCategoryId,
                t.IsTransfer,
                t.TransferGroupId,
                t.ImportBatchId,
                t.CreatedUtc,
                t.CategorizationSource,
                t.NeedsReview,
                t.LlmConfidence,
                t.AppliedRuleId,
                t.SourceRowHash,
                t.LlmModel,
                t.CategorizedUtc,
                t.Note))
            .ToListAsync(ct);

        return Ok(new PagedResult<TransactionDto>(items, totalCount, page, pageSize));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TransactionDto>> UpdateCategory(
        Guid id,
        UpdateTransactionCategoryRequest request,
        CancellationToken ct)
    {
        // No Status filter — both Pending and Committed rows can be categorized.
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tx is null) return NotFound();

        // The note can be edited on its own; always apply it.
        tx.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        // Only re-stamp categorization (and clear rule/review/AI markers) when the category
        // actually changes — a note-only edit must not blow away an existing rule attribution.
        var subCategoryId = request.CategoryId is null ? null : request.SubCategoryId;
        bool categoryChanged = tx.CategoryId != request.CategoryId || tx.SubCategoryId != subCategoryId;
        if (categoryChanged)
        {
            if (subCategoryId is not null)
            {
                var belongs = await _db.SubCategories
                    .AnyAsync(s => s.Id == subCategoryId.Value && s.CategoryId == request.CategoryId!.Value, ct);
                if (!belongs)
                    return BadRequest(new { title = "SubCategoryId must belong to CategoryId.", status = 400 });
            }

            var targetKind = request.CategoryId is null
                ? CategoryKind.User
                : await _db.Categories
                    .Where(c => c.Id == request.CategoryId.Value)
                    .Select(c => (CategoryKind?)c.Kind)
                    .FirstOrDefaultAsync(ct) ?? CategoryKind.User;

            tx.CategoryId = request.CategoryId;
            tx.SubCategoryId = subCategoryId;
            tx.IsTransfer = targetKind == CategoryKind.Transfer;
            tx.CategorizationSource = CategorizationSource.Manual;
            tx.NeedsReview = false;
            tx.AppliedRuleId = null;
            tx.CategorizedUtc = DateTime.UtcNow;
        }

        if (request.AccountId.HasValue)
        {
            bool accountExists = await _db.Accounts.AnyAsync(a => a.Id == request.AccountId.Value, ct);
            if (!accountExists)
                return BadRequest(new { title = "AccountId not found.", status = 400 });
            tx.AccountId = request.AccountId.Value;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new TransactionDto(
            tx.Id, tx.AccountId, tx.Date, tx.PostedDate, tx.Amount,
            tx.Description, tx.Merchant, tx.CategoryId, tx.SubCategoryId, tx.IsTransfer,
            tx.TransferGroupId, tx.ImportBatchId, tx.CreatedUtc,
            tx.CategorizationSource, tx.NeedsReview, tx.LlmConfidence, tx.AppliedRuleId,
            Note: tx.Note));
    }

    private static List<Guid> ParseGuidList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
    }
}
