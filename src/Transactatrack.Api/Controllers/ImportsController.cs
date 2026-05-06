using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IImportService _importService;

    public ImportsController(AppDbContext db, IImportService importService)
    {
        _db = db;
        _importService = importService;
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
                _db.Transactions.Count(t => t.ImportBatchId == b.Id)))
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
            .Take(50)
            .Select(t => new ImportPreviewRowDto(t.Date, t.PostedDate, t.Amount, t.Description, false))
            .ToListAsync(ct);

        var totalCount = await _db.Transactions.CountAsync(t => t.ImportBatchId == id, ct);

        return Ok(new ImportBatchDetailDto(
            new ImportBatchDto(batch.Id, batch.AccountId, batch.BankCode, batch.OriginalFilename, batch.UploadedUtc, batch.Status, totalCount),
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
}

public record ImportBatchDetailDto(
    ImportBatchDto Batch,
    IReadOnlyList<ImportPreviewRowDto> Transactions
);
