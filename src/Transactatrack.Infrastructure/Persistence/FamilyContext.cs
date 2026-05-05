using Transactatrack.Application;
using Transactatrack.Domain.Common;

namespace Transactatrack.Infrastructure.Persistence;

public class FamilyContext : IFamilyContext
{
    public Guid ActiveFamilyId { get; set; } = SeedIds.DefaultFamilyId;
}
