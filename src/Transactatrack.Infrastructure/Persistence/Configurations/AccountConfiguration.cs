using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transactatrack.Domain.Entities;

namespace Transactatrack.Infrastructure.Persistence.Configurations;

internal class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Institution).HasMaxLength(200);
        builder.Property(a => a.BankCode).HasMaxLength(50);

        builder.HasIndex(a => a.FamilyId);

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(a => a.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Owner>()
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
