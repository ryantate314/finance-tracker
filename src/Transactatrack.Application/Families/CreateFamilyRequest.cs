using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Families;

public record CreateFamilyRequest(
    [Required, StringLength(200)] string Name
);
