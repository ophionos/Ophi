using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public const int NameMaxLength = 50;

    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(NameMaxLength);

        builder.Property(t => t.Color)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(t => t.Weight)
            .HasDefaultValue(0);

        builder.HasIndex(t => new { t.UserId, t.Name })
            .IsUnique();

        builder.HasMany(t => t.ProductTags)
            .WithOne(pt => pt.Tag)
            .HasForeignKey(pt => pt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
