using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    // Public so the API validators bound input by the same numbers (same limits as comparison groups).
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(NameMaxLength);

        builder.Property(b => b.Description)
            .HasMaxLength(DescriptionMaxLength);

        builder.HasIndex(b => b.UserId);

        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BasketItemConfiguration : IEntityTypeConfiguration<BasketItem>
{
    public void Configure(EntityTypeBuilder<BasketItem> builder)
    {
        builder.HasKey(i => new { i.BasketId, i.ProductId });

        builder.Property(i => i.Quantity)
            .HasDefaultValue(1);

        builder.HasOne(i => i.Basket)
            .WithMany(b => b.Items)
            .HasForeignKey(i => i.BasketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A product can be in several baskets; listing the baskets of a product (and the cascade on
        // product delete) goes through ProductId, which the composite key does not lead with.
        builder.HasIndex(i => i.ProductId);
    }
}
