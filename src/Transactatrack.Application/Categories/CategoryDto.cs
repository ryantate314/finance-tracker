using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.Categories;

public record CategoryDto(Guid Id, string Name, CategoryKind Kind, DateTime CreatedUtc, IReadOnlyList<SubCategoryDto> SubCategories);
