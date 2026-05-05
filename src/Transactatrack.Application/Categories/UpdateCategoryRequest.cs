using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Categories;

public record UpdateCategoryRequest(
    [Required, StringLength(200)] string Name
);
