using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    // Public so the API validators bound input by the same number; SQLite ignores varchar bounds,
    // Postgres turns a longer value into a 500.
    public const int HostMaxLength = 253;

    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Host)
            .IsRequired()
            .HasMaxLength(HostMaxLength);

        builder.Property(s => s.Currency)
            .IsRequired()
            .HasMaxLength(3);

        // Same precision as prices (PricePointConfiguration).
        builder.Property(s => s.Amount).HasPrecision(18, 2);
        builder.Property(s => s.FreeAbove).HasPrecision(18, 2);

        builder.HasIndex(s => new { s.UserId, s.Host })
            .IsUnique();

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
