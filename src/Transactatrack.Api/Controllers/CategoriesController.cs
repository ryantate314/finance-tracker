using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Categories;
using Transactatrack.Domain.Entities;
using Transactatrack.Domain.Enums;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db) => _db = db;

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> List()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Kind, c.CreatedUtc,
                _db.SubCategories
                    .Where(s => s.CategoryId == c.Id)
                    .OrderBy(s => s.Name)
                    .Select(s => new SubCategoryDto(s.Id, s.CategoryId, s.Name, s.CreatedUtc))
                    .ToList()))
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Get(Guid id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        var subs = await _db.SubCategories
            .Where(s => s.CategoryId == id)
            .OrderBy(s => s.Name)
            .Select(s => new SubCategoryDto(s.Id, s.CategoryId, s.Name, s.CreatedUtc))
            .ToListAsync();
        return Ok(new CategoryDto(category.Id, category.Name, category.Kind, category.CreatedUtc, subs));
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request)
    {
        var category = new Category { Name = request.Name };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = category.Id },
            new CategoryDto(category.Id, category.Name, category.Kind, category.CreatedUtc, []));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCategoryRequest request)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        if (category.Kind != CategoryKind.User)
            return Conflict(new { title = "System category cannot be renamed", status = 409 });
        category.Name = request.Name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        if (category.Kind != CategoryKind.User)
            return Conflict(new { title = "System category cannot be deleted", status = 409 });
        _db.Categories.Remove(category);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { title = "Category has dependent records", status = 409 }); }
        return NoContent();
    }

    // ── Sub-categories ────────────────────────────────────────────────────────

    [HttpPost("{categoryId:guid}/subcategories")]
    public async Task<ActionResult<SubCategoryDto>> CreateSub(Guid categoryId, CreateSubCategoryRequest request)
    {
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == categoryId);
        if (!categoryExists) return NotFound();

        var sub = new SubCategory { CategoryId = categoryId, Name = request.Name };
        _db.SubCategories.Add(sub);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = categoryId },
            new SubCategoryDto(sub.Id, sub.CategoryId, sub.Name, sub.CreatedUtc));
    }

    [HttpPut("{categoryId:guid}/subcategories/{id:guid}")]
    public async Task<IActionResult> UpdateSub(Guid categoryId, Guid id, UpdateSubCategoryRequest request)
    {
        var sub = await _db.SubCategories.FirstOrDefaultAsync(s => s.Id == id && s.CategoryId == categoryId);
        if (sub is null) return NotFound();
        sub.Name = request.Name;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{categoryId:guid}/subcategories/{id:guid}")]
    public async Task<IActionResult> DeleteSub(Guid categoryId, Guid id)
    {
        var sub = await _db.SubCategories.FirstOrDefaultAsync(s => s.Id == id && s.CategoryId == categoryId);
        if (sub is null) return NotFound();
        _db.SubCategories.Remove(sub);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { title = "Sub-category has dependent records", status = 409 }); }
        return NoContent();
    }
}
