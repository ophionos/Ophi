using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class WebhookTargetConfiguration : IEntityTypeConfiguration<WebhookTarget>
{
    public void Configure(EntityTypeBuilder<WebhookTarget> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(w => w.Url)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(w => w.Events)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            )
            .IsRequired();

        builder.Property(w => w.IsEnabled)
            .HasDefaultValue(true);

        builder.HasIndex(w => w.UserId);
    }
}
