using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class StoreConfigurationConfiguration : IEntityTypeConfiguration<StoreConfiguration>
{
    public void Configure(EntityTypeBuilder<StoreConfiguration> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StoreId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.DomainPatternsJson)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(s => s.SelectorsJson)
            .IsRequired()
            .HasMaxLength(10000);

        builder.Property(s => s.PriceLocale)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("en-US");

        builder.Property(s => s.RequiresJavaScript)
            .HasDefaultValue(false);

        // Unique constraint: one store ID per user
        builder.HasIndex(s => new { s.UserId, s.StoreId })
            .IsUnique();

        // Index for querying user's stores
        builder.HasIndex(s => s.UserId);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
