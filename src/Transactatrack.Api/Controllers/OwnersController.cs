using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Owners;
using Transactatrack.Domain.Entities;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/owners")]
public class OwnersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OwnersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OwnerDto>>> List()
    {
        var owners = await _db.Owners
            .OrderBy(o => o.Name)
            .Select(o => new OwnerDto(o.Id, o.FamilyId, o.Name, o.CreatedUtc))
            .ToListAsync();
        return Ok(owners);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerDto>> Get(Guid id)
    {
        var owner = await _db.Owners.FindAsync(id);
        if (owner is null) return NotFound();
        return Ok(new OwnerDto(owner.Id, owner.FamilyId, owner.Name, owner.CreatedUtc));
    }

    [HttpPost]
    public async Task<ActionResult<OwnerDto>> Create(CreateOwnerRequest request)
    {
        var owner = new Owner { Name = request.Name };
        _db.Owners.Add(owner);
        await _db.SaveChangesAsync();
        var dto = new OwnerDto(owner.Id, owner.FamilyId, owner.Name, owner.CreatedUtc);
        return CreatedAtAction(nameof(Get), new { id = owner.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateOwnerRequest request)
    {
        var owner = await _db.Owners.FindAsync(id);
        if (owner is null) return NotFound();
        owner.Name = request.Name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var owner = await _db.Owners.FindAsync(id);
        if (owner is null) return NotFound();
        _db.Owners.Remove(owner);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { title = "Owner has dependent records", status = 409 });
        }
        return NoContent();
    }
}
