using Transactatrack.Domain.Common;

namespace Transactatrack.Domain.Entities;

public class SubCategory : FamilyScopedEntity
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
}
