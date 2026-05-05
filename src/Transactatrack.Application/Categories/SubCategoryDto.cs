namespace Transactatrack.Application.Categories;

public record SubCategoryDto(Guid Id, Guid CategoryId, string Name, DateTime CreatedUtc);
