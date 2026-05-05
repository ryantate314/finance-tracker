using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Families;

public record UpdateFamilyRequest(
    [Required, StringLength(200)] string Name
);
