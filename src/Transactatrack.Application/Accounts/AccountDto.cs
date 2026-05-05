using Transactatrack.Domain.Enums;

namespace Transactatrack.Application.Accounts;

public record AccountDto(
    Guid Id,
    Guid FamilyId,
    Guid OwnerId,
    string Name,
    string? Institution,
    AccountType AccountType,
    string? BankCode,
    DateTime CreatedUtc
);
