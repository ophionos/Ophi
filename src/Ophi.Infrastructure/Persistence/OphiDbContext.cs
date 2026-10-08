using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence;

public class OphiDbContext(DbContextOptions<OphiDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUrl> ProductUrls => Set<ProductUrl>();
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ComparisonGroup> ComparisonGroups => Set<ComparisonGroup>();
    public DbSet<StoreConfiguration> StoreConfigurations => Set<StoreConfiguration>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ScrapeLog> ScrapeLogs => Set<ScrapeLog>();
    public DbSet<WebhookTarget> WebhookTargets => Set<WebhookTarget>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<StoreClearance> StoreClearances => Set<StoreClearance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OphiDbContext).Assembly);

        // Optimistic concurrency for alert firing. Two worker processes can each receive a
        // PriceUpdatedEvent for the same product and race to claim an alert; xmin (Postgres' system
        // row-version) makes the losing SaveChanges throw DbUpdateConcurrencyException instead of
        // double-firing. The worker handlers catch that and treat it as "already fired" (see
        // CheckAlertsHandler / SendAlertNotificationHandler). Postgres-only: xmin doesn't exist on
        // SQLite (the test tier), which never runs multiple writers, so this is gated on the provider
        // to keep EnsureCreated working there. No DDL — xmin is a system column.
        if (Database.IsNpgsql())
        {
            // Npgsql maps a uint, OnAddOrUpdate, concurrency-token property to the system "xmin" column
            // (no DDL — it's always present). A shadow property keeps Alert clean and the SQLite model
            // free of it. (EF Core 10 / Npgsql 10 dropped the old UseXminAsConcurrencyToken() helper.)
            modelBuilder.Entity<Alert>()
                .Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate();
        }

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Normalize all DateTime values to UTC at the storage boundary. Required by Npgsql
        // (timestamptz rejects non-Utc Kind on write); harmless on SQLite. See UtcDateTimeConverter.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
