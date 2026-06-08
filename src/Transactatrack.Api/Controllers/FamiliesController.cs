using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categories;
using Transactatrack.Application.CategoryRules;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/families")]
public class FamiliesController : ControllerBase
{
    private const int ExportVersion = 1;

    private readonly AppDbContext _db;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IFamilyImportService _importService;

    public FamiliesController(
        AppDbContext db,
        IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions,
        IFamilyImportService importService)
    {
        _db = db;
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _importService = importService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FamilyDto>>> List()
    {
        var families = await _db.Families
            .OrderBy(f => f.Name)
            .Select(f => new FamilyDto(f.Id, f.Name, f.CreatedUtc))
            .ToListAsync();
        return Ok(families);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FamilyDto>> Get(Guid id)
    {
        var family = await _db.Families.FindAsync(id);
        if (family is null) return NotFound();
        return Ok(new FamilyDto(family.Id, family.Name, family.CreatedUtc));
    }

    [HttpPost]
    public async Task<ActionResult<FamilyDto>> Create(CreateFamilyRequest request)
    {
        var family = new Family { Name = request.Name };
        _db.Families.Add(family);
        await _db.SaveChangesAsync();

        // System categories. /api/families is unscoped (no X-Family-Id), so we set
        // FamilyId explicitly; AppDbContext only auto-stamps when FamilyId is Guid.Empty.
        _db.Categories.Add(new Category { FamilyId = family.Id, Name = "Transfer", Kind = CategoryKind.Transfer });
        _db.Categories.Add(new Category { FamilyId = family.Id, Name = "Income", Kind = CategoryKind.Income });
        await _db.SaveChangesAsync();

        var dto = new FamilyDto(family.Id, family.Name, family.CreatedUtc);
        return CreatedAtAction(nameof(Get), new { id = family.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateFamilyRequest request)
    {
        var family = await _db.Families.FindAsync(id);
        if (family is null) return NotFound();
        family.Name = request.Name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        // /api/families is exempt from FamilyContextMiddleware, so ActiveFamilyId may be
        // Guid.Empty here. Use IgnoreQueryFilters and filter manually by the path id.
        var family = await _db.Families
            .Where(f => f.Id == id)
            .Select(f => new FamilyDto(f.Id, f.Name, f.CreatedUtc))
            .FirstOrDefaultAsync(ct);
        if (family is null) return NotFound();

        var owners = await _db.Owners.IgnoreQueryFilters()
            .Where(o => o.FamilyId == id)
            .OrderBy(o => o.Name)
            .Select(o => new OwnerDto(o.Id, o.FamilyId, o.Name, o.CreatedUtc))
            .ToListAsync(ct);

        var accounts = await _db.Accounts.IgnoreQueryFilters()
            .Where(a => a.FamilyId == id)
            .OrderBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.FamilyId, a.OwnerId, a.Name, a.Institution, a.AccountType, a.BankCode, a.CreatedUtc))
            .ToListAsync(ct);

        var categories = await _db.Categories.IgnoreQueryFilters()
            .Where(c => c.FamilyId == id)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Kind, c.CreatedUtc,
                _db.SubCategories.IgnoreQueryFilters()
                    .Where(s => s.CategoryId == c.Id)
                    .OrderBy(s => s.Name)
                    .Select(s => new SubCategoryDto(s.Id, s.CategoryId, s.Name, s.CreatedUtc))
                    .ToList()))
            .ToListAsync(ct);

        var rules = await _db.CategoryRules.IgnoreQueryFilters()
            .Where(r => r.FamilyId == id)
            .OrderBy(r => r.Priority)
            .Select(r => new CategoryRuleDto(
                r.Id, r.Priority, r.MatchField, r.MatchType, r.Pattern,
                r.AmountMin, r.AmountMax, r.TargetCategoryId, r.TargetSubCategoryId,
                r.Scope, r.AccountId, r.IsEnabled))
            .ToListAsync(ct);

        var batches = await _db.ImportBatches.IgnoreQueryFilters()
            .Where(b => b.FamilyId == id)
            .OrderBy(b => b.UploadedUtc)
            .Select(b => new ImportBatchDto(
                b.Id, b.AccountId, b.BankCode, b.OriginalFilename, b.UploadedUtc, b.Status,
                _db.Transactions.IgnoreQueryFilters().Count(t => t.ImportBatchId == b.Id),
                b.LlmStatus, b.LlmRowsTotal, b.LlmRowsDone))
            .ToListAsync(ct);

        // Include every transaction regardless of batch status — Pending/Discarded rows
        // belong to ImportBatch rows we're also exporting, so a future importer can
        // reconstruct the full state.
        var transactions = await _db.Transactions.IgnoreQueryFilters()
            .Where(t => t.FamilyId == id)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.CreatedUtc)
            .Select(t => new TransactionDto(
                t.Id, t.AccountId, t.Date, t.PostedDate, t.Amount,
                t.Description, t.Merchant, t.CategoryId, t.SubCategoryId, t.IsTransfer,
                t.TransferGroupId, t.ImportBatchId, t.CreatedUtc,
                t.CategorizationSource, t.NeedsReview, t.LlmConfidence, t.AppliedRuleId,
                t.SourceRowHash, t.LlmModel, t.CategorizedUtc, t.Note))
            .ToListAsync(ct);

        var dto = new FamilyExportDto(
            ExportVersion,
            DateTime.UtcNow,
            family,
            owners,
            accounts,
            categories,
            rules,
            batches,
            transactions);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(dto, _jsonOptions);
        var filename = $"transactatrack-{Slug(family.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", filename);
    }

    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<FamilyImportSummaryDto>> ImportNew(
        [FromBody] FamilyExportDto export,
        [FromQuery] string? name,
        CancellationToken ct)
    {
        var error = ValidateExport(export);
        if (error is not null) return BadRequest(new { title = error, status = 400 });
        var summary = await _importService.ImportAsNewAsync(export, name, ct);
        return Ok(summary);
    }

    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<FamilyImportSummaryDto>> Merge(
        Guid id,
        [FromBody] FamilyExportDto export,
        CancellationToken ct)
    {
        var error = ValidateExport(export);
        if (error is not null) return BadRequest(new { title = error, status = 400 });
        var summary = await _importService.MergeAsync(id, export, ct);
        if (summary is null) return NotFound();
        return Ok(summary);
    }

    private static string? ValidateExport(FamilyExportDto? export)
    {
        if (export is null) return "Request body is empty or malformed JSON.";
        if (export.ExportVersion != 1) return $"Unsupported ExportVersion: {export.ExportVersion}.";
        if (export.Family is null) return "Export is missing the family record.";
        if (export.Owners is null || export.Accounts is null || export.Categories is null
            || export.CategoryRules is null || export.ImportBatches is null || export.Transactions is null)
            return "Export is missing one or more required collections.";
        return null;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "family" : slug;
    }

    [HttpGet("{id:guid}/delete-impact")]
    public async Task<ActionResult<FamilyDeleteImpactDto>> DeleteImpact(Guid id, CancellationToken ct)
    {
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (family is null) return NotFound();

        IQueryable<T> Scoped<T>() where T : Domain.Common.FamilyScopedEntity =>
            _db.Set<T>().IgnoreQueryFilters().Where(e => e.FamilyId == id);

        return Ok(new FamilyDeleteImpactDto(
            family.Id, family.Name,
            Owners:         await Scoped<Owner>().CountAsync(ct),
            Accounts:       await Scoped<Account>().CountAsync(ct),
            Categories:     await Scoped<Category>().CountAsync(ct),
            SubCategories:  await Scoped<SubCategory>().CountAsync(ct),
            CategoryRules:  await Scoped<CategoryRule>().CountAsync(ct),
            ImportBatches:  await Scoped<ImportBatch>().CountAsync(ct),
            Transactions:   await Scoped<Transaction>().CountAsync(ct)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool cascade = false, CancellationToken ct = default)
    {
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (family is null) return NotFound();

        if (cascade)
        {
            await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
            // FK-safe deletion order: leaves first, then their parents.
            await _db.Transactions.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.CategoryRules.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.ImportBatches.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.SubCategories.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.Categories.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.Accounts.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.Owners.IgnoreQueryFilters().Where(e => e.FamilyId == id).ExecuteDeleteAsync(ct);
            await _db.Families.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
            await dbTx.CommitAsync(ct);
            return NoContent();
        }

        _db.Families.Remove(family);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { title = "Family has dependent records. Pass ?cascade=true to delete it and all of its data.", status = 409 });
        }
        return NoContent();
    }
}
