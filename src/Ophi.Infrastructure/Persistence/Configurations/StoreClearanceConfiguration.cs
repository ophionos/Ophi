using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class StoreClearanceConfiguration : IEntityTypeConfiguration<StoreClearance>
{
    public void Configure(EntityTypeBuilder<StoreClearance> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Host).IsRequired().HasMaxLength(253);
        builder.Property(c => c.UserAgent).IsRequired().HasMaxLength(512);
        builder.Property(c => c.StorageState).IsRequired();

        // One clearance per user and host; a new solve replaces it.
        builder.HasIndex(c => new { c.UserId, c.Host }).IsUnique();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
