namespace Transactatrack.Application.Categories;

public record CategoryDto(Guid Id, string Name, DateTime CreatedUtc, IReadOnlyList<SubCategoryDto> SubCategories);
