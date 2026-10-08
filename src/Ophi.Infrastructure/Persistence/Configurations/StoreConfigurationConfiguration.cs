using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class StoreConfigurationConfiguration : IEntityTypeConfiguration<StoreConfiguration>
{
    // Public so the API validators bound input by the same numbers; a longer value passes SQLite
    // (which ignores varchar bounds) and fails Postgres with a 500.
    public const int DomainPatternsJsonMaxLength = 2000;
    public const int SelectorsJsonMaxLength = 10000;
    public const int PriceLocaleMaxLength = 10;

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
            .HasMaxLength(DomainPatternsJsonMaxLength);

        builder.Property(s => s.SelectorsJson)
            .IsRequired()
            .HasMaxLength(SelectorsJsonMaxLength);

        builder.Property(s => s.PriceLocale)
            .IsRequired()
            .HasMaxLength(PriceLocaleMaxLength)
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
