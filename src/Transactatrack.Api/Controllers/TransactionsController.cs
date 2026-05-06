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
                t.IsTransfer,
                t.TransferGroupId,
                t.ImportBatchId,
                t.CreatedUtc))
            .ToListAsync(ct);

        return Ok(new PagedResult<TransactionDto>(items, totalCount, page, pageSize));
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
