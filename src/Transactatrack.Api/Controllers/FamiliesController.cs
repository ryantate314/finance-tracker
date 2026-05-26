using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Families;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/families")]
public class FamiliesController : ControllerBase
{
    private readonly AppDbContext _db;

    public FamiliesController(AppDbContext db) => _db = db;

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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var family = await _db.Families.FindAsync(id);
        if (family is null) return NotFound();
        _db.Families.Remove(family);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { title = "Family has dependent records", status = 409 });
        }
        return NoContent();
    }
}
