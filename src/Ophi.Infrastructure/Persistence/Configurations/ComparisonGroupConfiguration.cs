using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence.Configurations;

public class ComparisonGroupConfiguration : IEntityTypeConfiguration<ComparisonGroup>
{
    public void Configure(EntityTypeBuilder<ComparisonGroup> builder)
    {
        builder.HasKey(cg => cg.Id);

        builder.Property(cg => cg.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cg => cg.Description)
            .HasMaxLength(500);

        builder.HasIndex(cg => new { cg.UserId, cg.Name })
            .IsUnique();
    }
}
