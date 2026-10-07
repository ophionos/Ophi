using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.ImageUrl)
            .HasMaxLength(2048);

        builder.Property(p => p.CurrentPrice)
            .HasPrecision(18, 2);

        builder.Property(p => p.PreviousPrice)
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.HasMany(p => p.ProductUrls)
            .WithOne(pu => pu.Product)
            .HasForeignKey(pu => pu.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PriceHistory)
            .WithOne(ph => ph.Product)
            .HasForeignKey(ph => ph.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Alerts)
            .WithOne(a => a.Product)
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.ComparisonGroup)
            .WithMany(cg => cg.Products)
            .HasForeignKey(p => p.ComparisonGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => new { p.UserId, p.Status });

        builder.OwnsMany(p => p.CustomFields, cf =>
        {
            cf.ToJson();
        });
    }
}
