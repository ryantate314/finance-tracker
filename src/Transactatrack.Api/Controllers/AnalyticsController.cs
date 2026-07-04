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

    // How a transaction behaves relative to the currently-scoped account set:
    //   Normal   — ordinary income/expense.
    //   Hidden   — an internal transfer whose other leg is also in scope; nets to zero, drop it.
    //   Crossing — a transfer with exactly one leg in scope; real money crossing the boundary
    //              (inflow => income, outflow => "transfers out").
    private enum TxClass { Normal, Hidden, Crossing }

    [HttpGet("category-breakdown")]
    public async Task<ActionResult<IReadOnlyList<CategoryBreakdownItemDto>>> CategoryBreakdown(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? accountIds,
        CancellationToken ct = default)
    {
        var accountIdList = ParseGuidList(accountIds);
        var crossingGroups = await ResolveCrossingGroupsAsync(accountIdList, ct);

        var baseQuery = BuildBaseQuery(accountIdList, from, to);
        var rows = await (
            from t in baseQuery
            join c in _db.Categories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new
            {
                t.CategoryId,
                CategoryName = c != null ? c.Name : null,
                t.Amount,
                t.IsTransfer,
                t.TransferGroupId,
            }).ToListAsync(ct);

        // Sum signed amounts per category so refunds net against expenses; keep only real expenses.
        // Key on Guid.Empty for the uncategorized bucket (real category ids are never empty).
        var byCategory = new Dictionary<Guid, (Guid? Id, string? Name, decimal Sum, int Count)>();
        decimal transfersOut = 0m;
        int transfersOutCount = 0;

        foreach (var r in rows)
        {
            switch (Classify(r.IsTransfer, r.TransferGroupId, crossingGroups))
            {
                case TxClass.Hidden:
                    continue;
                case TxClass.Crossing:
                    // Boundary-crossing money never pollutes a spending category; only the
                    // outflow side surfaces, as a single synthetic "Transfers out" row.
                    if (r.Amount < 0) { transfersOut += -r.Amount; transfersOutCount++; }
                    continue;
                default:
                    var catKey = r.CategoryId ?? Guid.Empty;
                    var cur = byCategory.GetValueOrDefault(catKey);
                    byCategory[catKey] = (r.CategoryId, r.CategoryName, cur.Sum + r.Amount, cur.Count + 1);
                    break;
            }
        }

        var items = byCategory
            .Where(kv => kv.Value.Sum < 0)
            .Select(kv => new CategoryBreakdownItemDto(
                kv.Value.Id,
                kv.Value.Name ?? "Uncategorized",
                Math.Abs(kv.Value.Sum),
                kv.Value.Count))
            .OrderByDescending(x => x.Amount)
            .ToList();

        if (transfersOut > 0)
            items.Add(new CategoryBreakdownItemDto(null, "Transfers out", transfersOut, transfersOutCount, IsTransfersBucket: true));

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
        var crossingGroups = await ResolveCrossingGroupsAsync(accountIdList, ct);

        var baseQuery = BuildBaseQuery(accountIdList, from, to);
        var rows = await (
            from t in baseQuery
            join c in _db.Categories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new
            {
                t.Date,
                t.Amount,
                t.IsTransfer,
                t.TransferGroupId,
                Kind = (CategoryKind?)(c != null ? c.Kind : (CategoryKind?)null),
            }).ToListAsync(ct);

        var byMonth = new Dictionary<(int Year, int Month), (decimal Income, decimal Expense, decimal In, decimal Out)>();
        foreach (var r in rows)
        {
            var cls = Classify(r.IsTransfer, r.TransferGroupId, crossingGroups);
            if (cls == TxClass.Hidden) continue;

            var key = (r.Date.Year, r.Date.Month);
            var cur = byMonth.GetValueOrDefault(key);
            if (cls == TxClass.Crossing)
            {
                if (r.Amount > 0) cur.In += r.Amount; else cur.Out += r.Amount;
            }
            else
            {
                // Income bucket: rows under an Income-kind category, plus not-yet-tagged positives
                // (so a paycheck shows as income before it's categorized). Everything else is expense.
                bool isIncome = r.Kind == CategoryKind.Income || (r.Kind == null && r.Amount > 0);
                if (isIncome) cur.Income += r.Amount; else cur.Expense += r.Amount;
            }
            byMonth[key] = cur;
        }

        var items = new List<MonthlyCashflowItemDto>();
        DateTime cursor = new(from.Value.Year, from.Value.Month, 1);
        DateTime end = new(to.Value.Year, to.Value.Month, 1);
        while (cursor <= end)
        {
            byMonth.TryGetValue((cursor.Year, cursor.Month), out var v);
            items.Add(new MonthlyCashflowItemDto(
                cursor.Year,
                cursor.Month,
                v.Income,
                v.Expense,
                v.Income + v.Expense + v.In + v.Out,
                v.In,
                v.Out));
            cursor = cursor.AddMonths(1);
        }

        return Ok(items);
    }

    [HttpGet("sankey")]
    public async Task<ActionResult<SankeyDto>> Sankey(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? accountIds,
        CancellationToken ct = default)
    {
        var accountIdList = ParseGuidList(accountIds);
        var scope = await ResolveScopeAsync(accountIdList, ct);

        // Every transfer leg in the family, so we can find an in-scope leg's counterpart account
        // even when that counterpart sits outside the current scope.
        var allLegs = await _db.Transactions
            .Where(t => t.TransferGroupId != null)
            .Select(t => new { GroupId = t.TransferGroupId!.Value, t.AccountId })
            .ToListAsync(ct);
        var legsByGroup = allLegs
            .GroupBy(l => l.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AccountId).ToList());

        var accounts = await _db.Accounts.Select(a => new { a.Id, a.Name, a.OwnerId, a.AccountType }).ToListAsync(ct);
        var accountById = accounts.ToDictionary(a => a.Id, a => (a.Name, a.OwnerId, a.AccountType));
        var ownerName = await _db.Owners.ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var catName = await _db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var baseQuery = BuildBaseQuery(accountIdList, from, to);
        var rows = await (
            from t in baseQuery
            join c in _db.Categories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new
            {
                t.AccountId,
                t.Amount,
                t.CategoryId,
                Kind = (CategoryKind?)(c != null ? c.Kind : (CategoryKind?)null),
                t.IsTransfer,
                t.TransferGroupId,
            }).ToListAsync(ct);

        string AccountNode(Guid id) => $"account:{id}";
        string CategoryNode(Guid? id) => id is null ? "category:uncategorized" : $"category:{id}";
        string IncomeNode(Guid accountId) =>
            $"income:{(accountById.TryGetValue(accountId, out var a) ? a.OwnerId : Guid.Empty)}";
        // A positive on a credit-card / loan account is a payment or refund, never earned income.
        bool IsLiability(Guid accountId) =>
            accountById.TryGetValue(accountId, out var a) &&
            (a.AccountType == AccountType.CreditCard || a.AccountType == AccountType.Loan);

        var links = new Dictionary<(string Source, string Target), decimal>();
        void Add(string s, string t, decimal v)
        {
            if (v <= 0) return;
            links[(s, t)] = links.GetValueOrDefault((s, t)) + v;
        }

        // Net per account+category so refunds reduce the expense flow.
        var expenseNet = new Dictionary<(Guid Account, Guid? Category), decimal>();
        void AddExpense(Guid account, Guid? category, decimal signedAmount)
        {
            var key = (account, category);
            expenseNet[key] = expenseNet.GetValueOrDefault(key) + signedAmount;
        }

        foreach (var r in rows)
        {
            bool transferish = r.IsTransfer || r.TransferGroupId is not null;
            if (transferish)
            {
                Guid? counterpart = null;
                if (r.TransferGroupId is not null && legsByGroup.TryGetValue(r.TransferGroupId.Value, out var legAccts))
                    counterpart = legAccts.FirstOrDefault(a => a != r.AccountId);
                if (counterpart == Guid.Empty) counterpart = null;

                if (r.Amount < 0)
                {
                    // Outflow leg: money leaving this account toward its counterpart (the personal→family flow).
                    string target = counterpart is not null ? AccountNode(counterpart.Value) : "transfersout";
                    Add(AccountNode(r.AccountId), target, -r.Amount);
                }
                else
                {
                    // Inflow leg. With an in-scope counterpart the outflow leg already drew the
                    // account→account link, so skip it here. An out-of-scope counterpart draws from
                    // that account; an unmatched transfer (no counterpart) comes from "Transfers in"
                    // — NOT income, since a transfer is not earned money.
                    if (counterpart is null)
                        Add("transfersin", AccountNode(r.AccountId), r.Amount);
                    else if (!scope.Contains(counterpart.Value))
                        Add(AccountNode(counterpart.Value), AccountNode(r.AccountId), r.Amount);
                }
                continue;
            }

            if (r.Amount > 0)
            {
                bool earnedIncome = r.Kind == CategoryKind.Income || (r.Kind is null && !IsLiability(r.AccountId));
                if (earnedIncome)
                    Add(IncomeNode(r.AccountId), AccountNode(r.AccountId), r.Amount);
                else if (r.Kind is null)
                    // Uncategorized positive on a credit card/loan = a payment or refund coming in.
                    Add("transfersin", AccountNode(r.AccountId), r.Amount);
                else
                    // A refund sitting in a real spending category nets against that category's outflow.
                    AddExpense(r.AccountId, r.CategoryId, r.Amount);
            }
            else
            {
                AddExpense(r.AccountId, r.CategoryId, r.Amount);
            }
        }

        foreach (var kv in expenseNet)
            if (kv.Value < 0)
                Add(AccountNode(kv.Key.Account), CategoryNode(kv.Key.Category), -kv.Value);

        NetAccountFlows(links);

        SankeyNodeDto ResolveNode(string id)
        {
            if (id == "transfersout") return new(id, "Transfers out", "sink");
            if (id == "transfersin") return new(id, "Transfers in", "source");
            int sep = id.IndexOf(':');
            string kind = id[..sep];
            string rest = id[(sep + 1)..];
            return kind switch
            {
                "account" => new(id, accountById.TryGetValue(Guid.Parse(rest), out var a) ? a.Name : "Account", "account"),
                "income" => new(id, (ownerName.TryGetValue(Guid.Parse(rest), out var o) ? o : "Owner") + " income", "income"),
                "category" when rest == "uncategorized" => new(id, "Uncategorized", "category"),
                "category" => new(id, catName.TryGetValue(Guid.Parse(rest), out var c) ? c : "Category", "category"),
                _ => new(id, id, "account"),
            };
        }

        var nodeIds = links.Keys.SelectMany(k => new[] { k.Source, k.Target }).Distinct();
        var nodes = nodeIds.Select(ResolveNode).ToList();
        var linkDtos = links.Select(kv => new SankeyLinkDto(kv.Key.Source, kv.Key.Target, kv.Value)).ToList();

        return Ok(new SankeyDto(nodes, linkDtos));
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

    /// <summary>The concrete set of accounts that defines the analytics boundary.</summary>
    private async Task<HashSet<Guid>> ResolveScopeAsync(IReadOnlyList<Guid> accountIdList, CancellationToken ct)
    {
        if (accountIdList.Count > 0) return accountIdList.ToHashSet();
        return (await _db.Accounts.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>
    /// Transfer groups straddling the boundary — at least one leg inside the scope and at least one
    /// outside. Membership is account-based and date-window-independent, so a leg landing a day
    /// outside the requested range still nets correctly.
    /// </summary>
    private async Task<HashSet<Guid>> ResolveCrossingGroupsAsync(IReadOnlyList<Guid> accountIdList, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(accountIdList, ct);

        var legs = await _db.Transactions
            .Where(t => t.TransferGroupId != null)
            .Select(t => new { GroupId = t.TransferGroupId!.Value, t.AccountId })
            .ToListAsync(ct);

        return legs
            .GroupBy(l => l.GroupId)
            .Where(g => g.Any(x => scope.Contains(x.AccountId)) && g.Any(x => !scope.Contains(x.AccountId)))
            .Select(g => g.Key)
            .ToHashSet();
    }

    private static TxClass Classify(bool isTransfer, Guid? groupId, HashSet<Guid> crossingGroups)
    {
        if (!isTransfer && groupId is null) return TxClass.Normal;
        // A tagged transfer with no matched counterpart can't be proven to net internally.
        if (groupId is null) return TxClass.Crossing;
        return crossingGroups.Contains(groupId.Value) ? TxClass.Crossing : TxClass.Hidden;
    }

    /// <summary>Collapse opposing account→account flows (A→B 500 and B→A 200 ⇒ A→B 300) to keep the diagram acyclic.</summary>
    private static void NetAccountFlows(Dictionary<(string Source, string Target), decimal> links)
    {
        var accountPairs = links.Keys
            .Where(k => k.Source.StartsWith("account:") && k.Target.StartsWith("account:"))
            .ToList();

        var done = new HashSet<(string, string)>();
        foreach (var (s, t) in accountPairs)
        {
            if (done.Contains((s, t))) continue;
            done.Add((s, t));
            done.Add((t, s));

            decimal fwd = links.GetValueOrDefault((s, t));
            decimal back = links.GetValueOrDefault((t, s));
            if (back == 0) continue;

            links.Remove((s, t));
            links.Remove((t, s));
            decimal net = fwd - back;
            if (net > 0) links[(s, t)] = net;
            else if (net < 0) links[(t, s)] = -net;
        }
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
