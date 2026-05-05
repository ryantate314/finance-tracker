using Transactatrack.Domain.Common;

namespace Transactatrack.Domain.Entities;

public class Category : FamilyScopedEntity
{
    public string Name { get; set; } = string.Empty;
}
