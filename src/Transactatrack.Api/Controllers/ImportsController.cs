using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Categorization;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IImportService _importService;
    private readonly ICategorizationService _categorization;

    public ImportsController(AppDbContext db, IImportService importService, ICategorizationService categorization)
    {
        _db = db;
        _importService = importService;
        _categorization = categorization;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ImportBatchDto>>> List(CancellationToken ct)
    {
        var batches = await _db.ImportBatches
            .OrderByDescending(b => b.UploadedUtc)
            .Select(b => new ImportBatchDto(
                b.Id,
                b.AccountId,
                b.BankCode,
                b.OriginalFilename,
                b.UploadedUtc,
                b.Status,
                _db.Transactions.Count(t => t.ImportBatchId == b.Id),
                b.LlmStatus,
                b.LlmRowsTotal,
                b.LlmRowsDone))
            .ToListAsync(ct);
        return Ok(batches);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImportBatchDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();

        var transactions = await _db.Transactions
            .Where(t => t.ImportBatchId == id)
            .OrderByDescending(t => t.Date)
            .Select(t => new ImportPreviewRowDto(t.Date, t.PostedDate, t.Amount, t.Description, false, t.CategoryId, t.SubCategoryId, t.CategorizationSource, t.NeedsReview, t.Id, t.AppliedRuleId))
            .ToListAsync(ct);

        var totalCount = transactions.Count;

        return Ok(new ImportBatchDetailDto(
            new ImportBatchDto(batch.Id, batch.AccountId, batch.BankCode, batch.OriginalFilename, batch.UploadedUtc, batch.Status, totalCount, batch.LlmStatus, batch.LlmRowsTotal, batch.LlmRowsDone),
            transactions));
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<ImportPreviewDto>> Upload(
        [FromForm] Guid accountId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { title = "File is required", status = 400 });

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await _importService.UploadAsync(accountId, stream, file.FileName, ct);
            return Ok(preview);
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }

    [HttpPost("{id:guid}/commit")]
    public async Task<IActionResult> Commit(Guid id, CancellationToken ct)
    {
        try
        {
            await _importService.CommitAsync(id, ct);
            return NoContent();
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }

    [HttpPost("{id:guid}/discard")]
    public async Task<IActionResult> Discard(Guid id, CancellationToken ct)
    {
        try
        {
            await _importService.DiscardAsync(id, ct);
            return NoContent();
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _importService.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }

    [HttpPost("{id:guid}/rerun-rules")]
    public async Task<IActionResult> RerunRules(Guid id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();
        if (batch.Status != ImportBatchStatus.Pending)
            return Conflict(new { title = $"Batch is in status {batch.Status}; only Pending batches can have rules re-run.", status = 409 });

        await _categorization.RerunRulesAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/suggest-llm")]
    public async Task<IActionResult> SuggestLlm(Guid id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();
        if (batch.Status != ImportBatchStatus.Pending)
            return Conflict(new { title = $"Batch is in status {batch.Status}; only Pending batches can receive LLM suggestions.", status = 409 });
        if (batch.LlmStatus == LlmCategorizationStatus.Running)
            return Conflict(new { title = "LLM categorization is already running for this batch.", status = 409 });

        await _categorization.StartLlmAsync(id, ct);
        return Accepted();
    }
}

public record ImportBatchDetailDto(
    ImportBatchDto Batch,
    IReadOnlyList<ImportPreviewRowDto> Transactions
);
