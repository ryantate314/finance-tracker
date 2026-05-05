using System.ComponentModel.DataAnnotations;

namespace Transactatrack.Application.Owners;

public record CreateOwnerRequest(
    [Required, StringLength(200)] string Name
);
