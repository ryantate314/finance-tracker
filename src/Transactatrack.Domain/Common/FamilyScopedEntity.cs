namespace Transactatrack.Domain.Common;

public abstract class FamilyScopedEntity
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public DateTime CreatedUtc { get; set; }
}
