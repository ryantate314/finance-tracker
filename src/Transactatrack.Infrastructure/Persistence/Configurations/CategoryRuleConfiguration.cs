using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence.Configurations;

internal class CategoryRuleConfiguration : IEntityTypeConfiguration<CategoryRule>
{
    public void Configure(EntityTypeBuilder<CategoryRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Pattern).IsRequired().HasMaxLength(500);
        builder.Property(r => r.AmountMin).HasPrecision(18, 4);
        builder.Property(r => r.AmountMax).HasPrecision(18, 4);

        builder.HasIndex(r => r.FamilyId);

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(r => r.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(r => r.TargetCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
