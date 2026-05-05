using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence.Configurations;

internal class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();
        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);

        builder.HasIndex(o => o.FamilyId);

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(o => o.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
