using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Ophi.Worker.Settings;
using Wolverine;


namespace Ophi.Worker.Services;

internal record DueProductUrlCandidate(
    Guid Id,
    DateTime? LastCheckedAt,
    int? ProductCheckIntervalMinutes,
    int? UserDefaultCheckIntervalMinutes,
    int? UserScrapeCacheTtlMinutes);

public class PriceCheckDispatcher(
    IServiceProvider serviceProvider,
    DataRetentionService dataRetentionService,
    ExchangeRateRefresher exchangeRateRefresher,
    IOptions<WorkerSettings> workerSettings,
    TimeProvider timeProvider,
    ILogger<PriceCheckDispatcher> logger) : BackgroundService
{
    private readonly WorkerSettings _settings = workerSettings.Value;

    private static readonly string HeartbeatPath =
        Environment.GetEnvironmentVariable("WORKER_HEARTBEAT_PATH")
            ?? Path.Combine(Path.GetTempPath(), "ophi-worker.heartbeat");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Price check dispatcher started (Interval: {Interval}s, BatchSize: {BatchSize})",
            _settings.DispatchIntervalSeconds, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WriteHeartbeat();
                await dataRetentionService.CleanupIfDueAsync(stoppingToken);
                await exchangeRateRefresher.RefreshIfDueAsync(stoppingToken);
                await DispatchPendingProductsAsync(stoppingToken);
                await DispatchDueProductUrlsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in price check dispatcher");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.DispatchIntervalSeconds), stoppingToken);
        }

        logger.LogInformation("Price check dispatcher stopped");
    }

    private void WriteHeartbeat()
    {
        try
        {
            File.WriteAllText(HeartbeatPath, timeProvider.GetUtcNow().UtcDateTime.ToString("O"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write heartbeat file at {Path}", HeartbeatPath);
        }
    }

    // Reconciliation backstop. Since Phase 2, the instant trigger for a new product's first scrape is
    // the API's PublishAsync(ScrapeProductUrlCommand) — delivered durably over the Postgres transport
    // (split) or handled in-process (embedded). This poll is the defense-in-depth net: it re-publishes
    // for any product still Pending (e.g. a publish lost to a crash between commit and send). The
    // handler's "!Force && Status != Pending" guard makes a duplicate delivery a no-op, so running the
    // poll alongside the transport is idempotent. Note: the command it publishes routes to the LOCAL
    // [LocalQueue("scraping")] handler in this worker process — only the SplitApi config routes
    // ScrapeProductUrlCommand back out to the Postgres queue, so there is no send-back-out loop here.
    private async Task DispatchPendingProductsAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Dispatch per-ProductUrl for pending products
        var pendingProductUrls = await dbContext.ProductUrls
            .Where(pu => pu.Product.Status == ProductStatus.Pending)
            .OrderBy(pu => pu.CreatedAt)
            .Take(_settings.BatchSize)
            .Select(pu => pu.Id)
            .ToListAsync(cancellationToken);

        if (pendingProductUrls.Count == 0)
            return;

        logger.LogInformation("Dispatching initial scrape for {Count} pending product URLs", pendingProductUrls.Count);

        foreach (var productUrlId in pendingProductUrls)
        {
            await messageBus.PublishAsync(new ScrapeProductUrlCommand(productUrlId));
        }
    }

    // Minimum check interval enforced by the settings validator. Anything below this is invalid
    // input, so it's safe to use as the SQL-level over-fetch threshold: any due URL must have
    // LastCheckedAt older than this floor (or null).
    private const int MinCheckIntervalMinutes = 15;

    // Over-fetch multiplier for stage-1 candidate scan. We pull this many * batchSize candidates
    // ordered by LastCheckedAt ascending so the oldest checks come first; the per-URL cascade is
    // then re-checked in memory. 5x is enough for realistic mixes of per-product / per-user / default
    // intervals — the oldest unchecked URLs almost always satisfy any cascade.
    private const int CandidateOverFetchMultiplier = 5;

    /// <summary>
    /// Stage 1: build an index-friendly query that returns candidate URLs whose `LastCheckedAt`
    /// is either null or older than the validator-floor interval, and whose failure backoff has
    /// ended. The per-URL cascade (CheckIntervalMinutes -> DefaultCheckIntervalMinutes -> 60, plus ScrapeCacheTtlMinutes)
    /// is *not* applied here because it would defeat the LastCheckedAt index on SQLite.
    /// Apply <see cref="IsDue"/> in memory to materialized candidates.
    /// </summary>
    internal static IQueryable<DueProductUrlCandidate> BuildCandidateQuery(
        IQueryable<ProductUrl> productUrls, DateTime now, int batchSize)
    {
        var staleCutoff = now.AddMinutes(-MinCheckIntervalMinutes);

        return productUrls
            .Where(pu => pu.Product.Status == ProductStatus.Active)
            .Where(pu => pu.Status != ProductUrlStatus.Paused)
            .Where(pu => pu.LastCheckedAt == null || pu.LastCheckedAt < staleCutoff)
            // Backoff must be filtered here, not in IsDue: a backed-off URL is never re-stamped, so it
            // stays the oldest candidate, and enough of them would fill the over-fetch window and
            // starve every URL that is really due.
            .Where(pu => pu.BackoffUntil == null || pu.BackoffUntil <= now)
            .OrderBy(pu => pu.LastCheckedAt)
            .Take(batchSize * CandidateOverFetchMultiplier)
            .Select(pu => new DueProductUrlCandidate(
                pu.Id,
                pu.LastCheckedAt,
                pu.Product.CheckIntervalMinutes,
                pu.Product.User.DefaultCheckIntervalMinutes,
                pu.Product.User.ScrapeCacheTtlMinutes));
    }

    /// <summary>
    /// Stage 2: apply the per-URL cascade (CheckIntervalMinutes ?? DefaultCheckIntervalMinutes ?? 60)
    /// and the optional ScrapeCacheTtlMinutes floor against a candidate row.
    /// </summary>
    internal static bool IsDue(DueProductUrlCandidate candidate, DateTime now)
    {
        if (candidate.LastCheckedAt is null)
            return true;

        var resolvedInterval =
            candidate.ProductCheckIntervalMinutes
            ?? candidate.UserDefaultCheckIntervalMinutes
            ?? 60;

        if (candidate.LastCheckedAt >= now.AddMinutes(-resolvedInterval))
            return false;

        if (candidate.UserScrapeCacheTtlMinutes is int ttl &&
            candidate.LastCheckedAt >= now.AddMinutes(-ttl))
        {
            return false;
        }

        return true;
    }

    internal static async Task<List<Guid>> SelectDueProductUrlIdsAsync(
        IQueryable<ProductUrl> productUrls, DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var candidates = await BuildCandidateQuery(productUrls, now, batchSize).ToListAsync(cancellationToken);

        return candidates
            .Where(c => IsDue(c, now))
            .Take(batchSize)
            .Select(c => c.Id)
            .ToList();
    }

    private async Task DispatchDueProductUrlsAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var dueProductUrlIds = await SelectDueProductUrlIdsAsync(
            dbContext.ProductUrls, now, _settings.BatchSize, cancellationToken);

        if (dueProductUrlIds.Count == 0)
        {
            logger.LogDebug("No product URLs due for price check");
            return;
        }

        logger.LogInformation("Dispatching price checks for {Count} product URLs", dueProductUrlIds.Count);

        // Stamp LastCheckedAt before dispatching to prevent re-dispatch if the process restarts
        // mid-loop. A crash between stamp and publish means at most one skipped check cycle,
        // which is acceptable. Without this, a crash would re-queue all URLs immediately.
        await dbContext.ProductUrls
            .Where(pu => dueProductUrlIds.Contains(pu.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(pu => pu.LastCheckedAt, now), cancellationToken);

        foreach (var productUrlId in dueProductUrlIds)
        {
            await messageBus.PublishAsync(new CheckProductUrlPriceCommand(productUrlId));
        }

        logger.LogDebug("Dispatched {Count} price check commands", dueProductUrlIds.Count);
    }
}
