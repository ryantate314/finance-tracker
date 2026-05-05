using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Owners;

public record UpdateOwnerRequest(
    [Required, StringLength(200)] string Name
);
