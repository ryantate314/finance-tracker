using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Categories;

public record CreateSubCategoryRequest(
    [Required, StringLength(200)] string Name
);
