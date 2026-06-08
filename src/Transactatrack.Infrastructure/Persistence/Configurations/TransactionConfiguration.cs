using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence.Configurations;

internal class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.Description).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Merchant).HasMaxLength(200);
        builder.Property(t => t.Note).HasMaxLength(1000);
        builder.Property(t => t.Amount).HasPrecision(18, 4);
        builder.Property(t => t.SourceRowHash).IsRequired().HasMaxLength(80);

        builder.Property(t => t.CategorizationSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(t => t.LlmConfidence).HasPrecision(3, 2);
        builder.Property(t => t.LlmModel).HasMaxLength(100);

        builder.HasIndex(t => new { t.FamilyId, t.AccountId, t.Date });
        builder.HasIndex(t => new { t.AccountId, t.SourceRowHash }).IsUnique();
        builder.HasIndex(t => new { t.FamilyId, t.NeedsReview });

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(t => t.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(t => t.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubCategory>()
            .WithMany()
            .HasForeignKey(t => t.SubCategoryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<CategoryRule>()
            .WithMany()
            .HasForeignKey(t => t.AppliedRuleId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
