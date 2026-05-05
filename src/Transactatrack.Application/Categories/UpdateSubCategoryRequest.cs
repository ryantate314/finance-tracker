using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Categories;

public record UpdateSubCategoryRequest(
    [Required, StringLength(200)] string Name
);
