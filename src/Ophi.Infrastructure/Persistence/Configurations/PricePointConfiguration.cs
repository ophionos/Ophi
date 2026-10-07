using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class PricePointConfiguration : IEntityTypeConfiguration<PricePoint>
{
    public void Configure(EntityTypeBuilder<PricePoint> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Price)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.HasIndex(p => new { p.ProductId, p.RecordedAt });
        builder.HasIndex(p => new { p.ProductId, p.Price });
        builder.HasIndex(p => new { p.ProductUrlId, p.RecordedAt });

        builder.HasOne(p => p.Product)
            .WithMany(p => p.PriceHistory)
            .HasForeignKey(pp => pp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProductUrl>()
            .WithMany()
            .HasForeignKey(pp => pp.ProductUrlId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
