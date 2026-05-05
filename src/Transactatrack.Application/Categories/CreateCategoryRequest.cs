using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Categories;

public record CreateCategoryRequest(
    [Required, StringLength(200)] string Name
);
