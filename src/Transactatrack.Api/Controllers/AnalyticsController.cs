using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Analytics;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db) => _db = db;

    [HttpGet("category-breakdown")]
    public async Task<ActionResult<IReadOnlyList<CategoryBreakdownItemDto>>> CategoryBreakdown(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? accountIds,
        CancellationToken ct = default)
    {
        var accountIdList = ParseGuidList(accountIds);

        // Sum signed amounts per category so refunds (positive) net against expenses (negative)
        // in the same category. Then keep only categories that net to a real expense.
        var query = BuildBaseQuery(accountIdList, from, to)
            .Where(t => !t.IsTransfer);

        var grouped = await (
            from t in query
            join c in _db.Categories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            group new { t.Amount, c } by new { t.CategoryId, CategoryName = c != null ? c.Name : null } into g
            select new
            {
                g.Key.CategoryId,
                g.Key.CategoryName,
                Amount = g.Sum(x => x.Amount),
                TransactionCount = g.Count()
            }).ToListAsync(ct);

        var items = grouped
            .Where(g => g.Amount < 0)
            .Select(g => new CategoryBreakdownItemDto(
                g.CategoryId,
                g.CategoryName ?? "Uncategorized",
                Math.Abs(g.Amount),
                g.TransactionCount))
            .OrderByDescending(x => x.Amount)
            .ToList();

        return Ok(items);
    }

    [HttpGet("monthly-cashflow")]
    public async Task<ActionResult<IReadOnlyList<MonthlyCashflowItemDto>>> MonthlyCashflow(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? accountIds,
        CancellationToken ct = default)
    {
        if (!from.HasValue || !to.HasValue)
            return BadRequest(new { title = "from and to are required.", status = 400 });

        var accountIdList = ParseGuidList(accountIds);

        var query = BuildBaseQuery(accountIdList, from, to)
            .Where(t => !t.IsTransfer);

        var grouped = await query
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(t => t.Amount > 0).Sum(t => (decimal?)t.Amount) ?? 0m,
                Expense = g.Where(t => t.Amount < 0).Sum(t => (decimal?)t.Amount) ?? 0m
            })
            .ToListAsync(ct);

        var byKey = grouped.ToDictionary(g => (g.Year, g.Month));

        var items = new List<MonthlyCashflowItemDto>();
        DateTime cursor = new(from.Value.Year, from.Value.Month, 1);
        DateTime end = new(to.Value.Year, to.Value.Month, 1);
        while (cursor <= end)
        {
            byKey.TryGetValue((cursor.Year, cursor.Month), out var row);
            decimal income = row?.Income ?? 0m;
            decimal expense = row?.Expense ?? 0m;
            items.Add(new MonthlyCashflowItemDto(cursor.Year, cursor.Month, income, expense, income + expense));
            cursor = cursor.AddMonths(1);
        }

        return Ok(items);
    }

    private IQueryable<Domain.Entities.Transaction> BuildBaseQuery(
        IReadOnlyList<Guid> accountIdList,
        DateTime? from,
        DateTime? to)
    {
        var query =
            from t in _db.Transactions
            join b in _db.ImportBatches on t.ImportBatchId equals b.Id
            where b.Status == ImportBatchStatus.Committed
            select t;

        if (accountIdList.Count > 0)
            query = query.Where(t => accountIdList.Contains(t.AccountId));

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

        return query;
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
