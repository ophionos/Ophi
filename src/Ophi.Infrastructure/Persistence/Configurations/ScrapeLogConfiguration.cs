using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class ScrapeLogConfiguration : IEntityTypeConfiguration<ScrapeLog>
{
    public void Configure(EntityTypeBuilder<ScrapeLog> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Price)
            .HasPrecision(18, 2);

        builder.Property(s => s.Error)
            .HasMaxLength(2000);

        builder.Property(s => s.StoreDomain)
            .HasMaxLength(255);

        builder.HasIndex(s => new { s.StoreDomain, s.CreatedAt });

        builder.HasIndex(s => new { s.ProductId, s.CreatedAt });

        builder.HasIndex(s => s.CreatedAt);

        builder.HasOne(s => s.Product)
            .WithMany(p => p.ScrapeLogs)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.ProductUrl)
            .WithMany()
            .HasForeignKey(s => s.ProductUrlId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
