using Transactatrack.Domain.Common;

namespace Transactatrack.Domain.Entities;

public class Owner : FamilyScopedEntity
{
    public string Name { get; set; } = string.Empty;
}
