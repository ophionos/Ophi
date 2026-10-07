using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Infrastructure.Metrics;

/// <summary>
/// Background service that periodically refreshes Prometheus price gauges
/// from the database. Runs every 60 seconds.
/// </summary>
public class PriceMetricsCollector(
    IServiceScopeFactory scopeFactory,
    ILogger<PriceMetricsCollector> logger) : BackgroundService
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PriceMetricsCollector started — refreshing gauges every {Interval}s",
            RefreshInterval.TotalSeconds);

        // Initial refresh on startup
        await RefreshGaugesAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshGaugesAsync(stoppingToken);
        }
    }

    internal async Task RefreshGaugesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();

            // Clear previous gauge values to avoid stale series from deleted/renamed products
            AppMetrics.ProductPriceCurrent.Unpublish();
            AppMetrics.ProductPriceLowest.Unpublish();
            AppMetrics.ProductInfo.Unpublish();

            var products = await db.Products
                .AsNoTracking()
                .Where(p => p.Status == ProductStatus.Active && p.CurrentPrice != null)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.CurrentPrice,
                    LowestPrice = p.PriceHistory.Count != 0
                        ? p.PriceHistory.Min(pp => pp.Price)
                        : p.CurrentPrice,
                    Store = p.ProductUrls
                        .OrderBy(u => u.CreatedAt)
                        .Select(u => u.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            foreach (var p in products)
            {
                var store = AppMetrics.ExtractStore(p.Store);
                var id = p.Id.ToString();
                var name = p.Name ?? "Unknown";

                if (p.CurrentPrice.HasValue)
                    AppMetrics.ProductPriceCurrent.WithLabels(id, store).Set((double)p.CurrentPrice.Value);

                if (p.LowestPrice.HasValue)
                    AppMetrics.ProductPriceLowest.WithLabels(id, store).Set((double)p.LowestPrice.Value);

                AppMetrics.ProductInfo.WithLabels(id, name).Set(1);
            }

            logger.LogDebug("Refreshed price gauges for {Count} products", products.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — expected
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh price metrics gauges");
        }
    }
}
