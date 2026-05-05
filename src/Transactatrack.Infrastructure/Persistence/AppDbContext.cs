using Microsoft.EntityFrameworkCore;
using Transactatrack.Application;
using Transactatrack.Domain.Common;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly IFamilyContext _familyContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, IFamilyContext familyContext)
        : base(options)
    {
        _familyContext = familyContext;
    }

    public DbSet<Family> Families => Set<Family>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Owner>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<Account>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<Category>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<SubCategory>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<Transaction>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<ImportBatch>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
        modelBuilder.Entity<CategoryRule>().HasQueryFilter(e => e.FamilyId == _familyContext.ActiveFamilyId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<FamilyScopedEntity>()
            .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.CreatedUtc = now;
            entry.Entity.FamilyId = _familyContext.ActiveFamilyId;
        }
        foreach (var entry in ChangeTracker.Entries<Family>()
            .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.CreatedUtc = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
