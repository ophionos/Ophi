using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class ProductUrlConfiguration : IEntityTypeConfiguration<ProductUrl>
{
    public void Configure(EntityTypeBuilder<ProductUrl> builder)
    {
        builder.HasKey(pu => pu.Id);

        builder.Property(pu => pu.Url)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(pu => pu.StoreId)
            .HasMaxLength(100);

        builder.Property(pu => pu.CurrentPrice)
            .HasPrecision(18, 2);

        builder.Property(pu => pu.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(pu => pu.Selector)
            .HasMaxLength(500);

        builder.HasIndex(pu => new { pu.ProductId, pu.Url })
            .IsUnique();

        builder.HasIndex(pu => pu.LastCheckedAt);
        builder.HasIndex(pu => new { pu.ProductId, pu.Status });

        builder.HasMany(pu => pu.PriceHistory)
            .WithOne(pp => pp.ProductUrl)
            .HasForeignKey(pp => pp.ProductUrlId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
