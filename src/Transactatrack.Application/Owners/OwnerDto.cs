namespace Transactatrack.Application.Owners;

public record OwnerDto(Guid Id, Guid FamilyId, string Name, DateTime CreatedUtc);
