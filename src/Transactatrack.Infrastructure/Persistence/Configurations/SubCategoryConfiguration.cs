using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence.Configurations;

internal class SubCategoryConfiguration : IEntityTypeConfiguration<SubCategory>
{
    public void Configure(EntityTypeBuilder<SubCategory> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);

        builder.HasIndex(s => s.FamilyId);
        builder.HasIndex(s => s.CategoryId);

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(s => s.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade: deleting a Category removes all its SubCategories.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
