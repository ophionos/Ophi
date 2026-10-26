using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class BasketAlertConfiguration : IEntityTypeConfiguration<BasketAlert>
{
    public void Configure(EntityTypeBuilder<BasketAlert> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TargetTotal)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(a => a.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(a => a.Host)
            .HasMaxLength(ShippingRateConfiguration.HostMaxLength);

        builder.HasIndex(a => new { a.BasketId, a.IsActive });
        builder.HasIndex(a => a.UserId);

        builder.HasOne(a => a.Basket)
            .WithMany()
            .HasForeignKey(a => a.BasketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
