using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TargetPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        // Mirrors Product.Currency (required, 3-char ISO code) so the two can be compared directly.
        builder.Property(a => a.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.HasIndex(a => new { a.UserId, a.ProductId, a.IsActive });
    }
}
