using Microsoft.AspNetCore.Mvc;
using Transactatrack.Application.Transfers;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public class TransfersController : ControllerBase
{
    private readonly ITransferMatcher _matcher;

    public TransfersController(ITransferMatcher matcher) => _matcher = matcher;

    /// <summary>Re-scan the whole committed ledger for unpaired transfers.</summary>
    [HttpPost("rescan")]
    public async Task<ActionResult<TransferMatchResult>> Rescan(CancellationToken ct)
    {
        var result = await _matcher.RescanFamilyAsync(ct);
        return Ok(result);
    }

    /// <summary>Manually link two transactions as a transfer pair.</summary>
    [HttpPost("link")]
    public async Task<IActionResult> Link(LinkTransferRequest request, CancellationToken ct)
    {
        try
        {
            Guid groupId = await _matcher.LinkAsync(request.TransactionIdA, request.TransactionIdB, ct);
            return Ok(new { transferGroupId = groupId });
        }
        catch (TransferException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }

    /// <summary>Break a transfer group, reverting both legs.</summary>
    [HttpPost("{groupId:guid}/unlink")]
    public async Task<IActionResult> Unlink(Guid groupId, CancellationToken ct)
    {
        try
        {
            await _matcher.UnlinkAsync(groupId, ct);
            return NoContent();
        }
        catch (TransferException ex)
        {
            return StatusCode(ex.StatusCode, new { title = ex.Title, status = ex.StatusCode });
        }
    }
}

public record LinkTransferRequest(Guid TransactionIdA, Guid TransactionIdB);
