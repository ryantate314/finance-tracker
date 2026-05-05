using System.ComponentModel.DataAnnotations;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.Accounts;

public record CreateAccountRequest(
    [Required] Guid OwnerId,
    [Required, StringLength(200)] string Name,
    [StringLength(200)] string? Institution,
    [Required] AccountType AccountType,
    [StringLength(50)] string? BankCode
);
