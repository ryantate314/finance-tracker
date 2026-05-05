using Transactatrack.Domain.Common;
using Transactatrack.Domain.Enums;

namespace Transactatrack.Domain.Entities;

public class Account : FamilyScopedEntity
{
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public AccountType AccountType { get; set; }
    public string? BankCode { get; set; }
}
