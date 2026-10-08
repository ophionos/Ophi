using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Services;
using Ophi.Worker.Settings;
using Wolverine;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

[WolverineHandler]
[LocalQueue("scraping")]
public static class CheckProductPriceHandler
{
    public static async Task<PriceUpdatedEvent?> HandleAsync(
        CheckProductUrlPriceCommand command,
        OphiDbContext dbContext,
        IScrapingService scrapingService,
        IOptions<WorkerSettings> workerSettings,
        IWebhookDispatchService webhookDispatchService,
        TimeProvider timeProvider,
        IMessageBus bus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var settings = workerSettings.Value;
        logger.LogDebug("Checking price for product URL {ProductUrlId}", command.ProductUrlId);

        var productUrl = await dbContext.ProductUrls
            .Include(pu => pu.Product)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(pu => pu.Id == command.ProductUrlId, cancellationToken);

        if (productUrl == null)
        {
            logger.LogWarning("ProductUrl {ProductUrlId} not found", command.ProductUrlId);
            return null;
        }

        var product = productUrl.Product;

        if (product.Status != ProductStatus.Active)
        {
            logger.LogDebug("Product {ProductId} is not active (status: {Status}), skipping",
                product.Id, product.Status);
            return null;
        }

        // Skip paused URLs
        if (productUrl.Status == ProductUrlStatus.Paused)
        {
            logger.LogDebug("ProductUrl {ProductUrlId} is paused, skipping", command.ProductUrlId);
            return null;
        }

        await PageFetchDelayHelper.ApplyAsync(product.User, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var result = await scrapingService.ScrapeProductAsync(
            productUrl.Url,
            productUrl.Selector,
            product.UserId,
            cancellationToken: cancellationToken);
        stopwatch.Stop();

        // Record scrape metrics
        var store = AppMetrics.ExtractStore(productUrl.Url);
        AppMetrics.ScrapeDurationSeconds.WithLabels(store).Observe(stopwatch.Elapsed.TotalSeconds);

        PriceUpdatedEvent? priceEvent;

        // Branch 1: Successful scrape with price, not out of stock
        if (result is { Success: true, Price: not null } && !result.IsOutOfStock)
        {
            AppMetrics.ScrapeTotal.WithLabels(store, "success").Inc();
            priceEvent = await HandleSuccessfulScrape(
                result, productUrl, product, stopwatch, settings, store, dbContext, timeProvider, logger, cancellationToken);
        }
        // Branch 2: Out of stock (may or may not have a price)
        else if (result.IsOutOfStock)
        {
            AppMetrics.ScrapeTotal.WithLabels(store, "out_of_stock").Inc();
            // Note: LastCheckedAt for OOS is stamped inside MarkOutOfStock — no separate write here.
            priceEvent = await HandleOutOfStock(
                result, productUrl, product, stopwatch, store, dbContext, timeProvider, logger, cancellationToken);
        }
        // Branch 3: Failure
        else
        {
            AppMetrics.ScrapeTotal.WithLabels(store, "failure").Inc();
            await HandleFailure(
                result, productUrl, product, stopwatch, settings, store, dbContext, webhookDispatchService, timeProvider, logger, cancellationToken);
            priceEvent = null;
        }

        // The scrape attempt resolved (success / OOS / failure) and the branch's writes — including any
        // OOS / back-in-stock notification — are committed. Ping the user's open streams to refetch.
        // Thin signal (no price data); short expiry so a durable-queue replay after a restart is dropped
        // rather than delivered minutes stale. Skipped on the early no-op returns above by design.
        await bus.PublishAsync(
            new LiveUpdate(product.UserId, LiveUpdate.ScrapeCompleted, product.Id),
            new DeliveryOptions { DeliverWithin = TimeSpan.FromSeconds(30) });

        return priceEvent;
    }

    private static async Task<PriceUpdatedEvent?> HandleSuccessfulScrape(
        ScrapingResult result,
        ProductUrl productUrl,
        Product product,
        Stopwatch stopwatch,
        WorkerSettings settings,
        string storeDomain,
        OphiDbContext dbContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Derive anomaly threshold: user setting (percent → ratio) or global fallback
        var anomalyThreshold = product.User.AnomalyThresholdPercent.HasValue
            ? product.User.AnomalyThresholdPercent.Value / 100m
            : settings.PriceAnomalyThreshold;

        // URL health analysis
        var analysis = ScrapeHealthAnalyzer.Analyze(
            productUrl.Url,
            result.FinalUrl,
            productUrl.CurrentPrice,
            result.Price!.Value,
            anomalyThreshold,
            result.PageTitle);

        // Set price anomaly flag (independent of redirect/soft-404 suspicion). The flag is recorded
        // per-URL and the product-level flag is derived from all live URLs: assigning
        // product.HasPriceAnomaly straight from this scrape let a clean scrape of one URL erase an
        // anomaly still present on a sibling that is also feeding the product's price.
        var priceAnomaly = ScrapeHealthAnalyzer.AnalyzePriceAnomaly(
            productUrl.CurrentPrice, result.Price!.Value, anomalyThreshold);
        productUrl.RecordPriceAnomaly(priceAnomaly.IsSuspicious);

        var siblingAnomaly = await AnyLiveSiblingHasAnomalyAsync(dbContext, productUrl, cancellationToken);
        product.RecomputePriceAnomaly(productUrl, siblingAnomaly);

        if (analysis.IsSuspicious)
        {
            productUrl.MarkSuspicious(analysis.Reason!);

            logger.LogWarning("Suspicious scrape for ProductUrl {ProductUrlId}: {Reason} (count: {Count}/{Max})",
                productUrl.Id, analysis.Reason, productUrl.SuspiciousCount, settings.MaxSuspiciousBeforePause);

            if (productUrl.SuspiciousCount >= settings.MaxSuspiciousBeforePause)
            {
                // Auto-pause: do NOT update price to avoid corruption
                productUrl.Pause();
                productUrl.MarkChecked(now);
                // The pause takes this URL's anomaly out of the product flag set above, and its
                // price out of the product MIN.
                product.RecomputePriceAnomaly(productUrl, siblingAnomaly);
                await ReaggregateWithoutAsync(dbContext, product, productUrl, cancellationToken);

                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = product.UserId,
                    ProductId = product.Id,
                    Title = Notification.BuildTitle("URL paused", product.Name),
                    Message = $"URL has been auto-paused after {productUrl.SuspiciousCount} consecutive suspicious scrapes. Reason: {analysis.Reason}",
                    Type = NotificationType.UrlHealth
                });

                dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildSuccessLog(
                    product.Id, productUrl.Id, result.Price.Value,
                    stopwatch.ElapsedMilliseconds, storeDomain));

                await dbContext.SaveChangesAsync(cancellationToken);
                return null;
            }

            if (productUrl.SuspiciousCount == 1)
            {
                // First warning — notify user but still update price
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = product.UserId,
                    ProductId = product.Id,
                    Title = Notification.BuildTitle("Suspicious scrape", product.Name),
                    Message = $"Detected suspicious activity on a tracked URL. Reason: {analysis.Reason}",
                    Type = NotificationType.UrlHealth
                });
            }

            // For warnings (count 1...N-1), fall through to update price normally
        }
        else
        {
            // Clean scrape — reset suspicious state
            productUrl.ClearSuspicious();
        }

        // Back-in-stock transition: was OOS, now has price
        var wasOutOfStock = productUrl.IsOutOfStock;
        var oldProductPrice = product.CurrentPrice;
        // Captured with the old price, before ApplyAggregate can re-anchor the product onto a
        // different currency. Without it the two halves of a percent-drop can silently be in
        // different denominations.
        var oldProductCurrency = product.Currency;

        productUrl.RecordSuccessfulScrape(result.Price.Value, result.Currency, now);

        if (wasOutOfStock)
        {
            dbContext.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = product.UserId,
                ProductId = product.Id,
                Title = Notification.BuildTitle("Back in stock", product.Name),
                Message = $"A tracked URL is back in stock at {result.Currency} {result.Price:F2}.",
                Type = NotificationType.BackInStock
            });
        }

        // Recalculate product-level prices: CurrentPrice = MIN across all live URLs.
        // Paused URLs are excluded: PriceCheckDispatcher skips them, so their price is frozen at
        // whatever it was when they were paused and only grows staler. Letting one keep defining
        // the MIN pins the product to a price that can never update again — and auto-pause fires
        // precisely on URLs the health analyzer already found untrustworthy.
        var otherUrlData = await dbContext.ProductUrls
            .Where(pu => pu.ProductId == product.Id
                && pu.Id != productUrl.Id
                && pu.Status != ProductUrlStatus.Paused
                && pu.CurrentPrice != null)
            .Select(pu => new { Price = pu.CurrentPrice!.Value, pu.Currency })
            .ToListAsync(cancellationToken);

        var allUrlPrices = otherUrlData
            .Select(u => new ProductPriceAggregator.UrlPrice(u.Price, u.Currency))
            .Append(new ProductPriceAggregator.UrlPrice(result.Price.Value, productUrl.Currency))
            .ToList();

        ProductPriceAggregator.ApplyAggregate(product, allUrlPrices);

        dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildSuccessLog(
            product.Id, productUrl.Id, result.Price.Value,
            stopwatch.ElapsedMilliseconds, storeDomain));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Updated price for {ProductName} (URL {ProductUrlId}): {Price} {Currency}",
            product.Name, productUrl.Id, result.Price, result.Currency);

        // Emit product-level OldPrice/NewPrice (MIN before/after aggregation) for consumers that
        // care about the headline price (alerts, price-changed webhook). Carry the just-scraped
        // URL value alongside so the per-URL history recorder logs the URL it actually scraped,
        // not the product MIN (which may belong to a different URL).
        return new PriceUpdatedEvent(
            product.Id,
            oldProductPrice,
            product.CurrentPrice!.Value,
            product.Currency,
            productUrl.Id,
            result.Price.Value,
            productUrl.Currency,
            oldProductCurrency);
    }

    private static async Task<PriceUpdatedEvent?> HandleOutOfStock(
        ScrapingResult result,
        ProductUrl productUrl,
        Product product,
        Stopwatch stopwatch,
        string storeDomain,
        OphiDbContext dbContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var wasInStock = productUrl.MarkOutOfStock(timeProvider.GetUtcNow().UtcDateTime, result.Currency);

        // Do NOT update CurrentPrice — preserve last known price

        dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildSuccessLog(
            product.Id, productUrl.Id, result.Price,
            stopwatch.ElapsedMilliseconds, storeDomain, isOutOfStock: true));

        // Notify on transition: in-stock → out-of-stock
        if (wasInStock)
        {
            dbContext.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = product.UserId,
                ProductId = product.Id,
                Title = Notification.BuildTitle("Out of stock", product.Name),
                Message = "A tracked URL appears to be out of stock.",
                Type = NotificationType.OutOfStock
            });

            logger.LogInformation("Product URL {ProductUrlId} ({ProductName}) is now out of stock",
                productUrl.Id, product.Name);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null; // No PriceUpdatedEvent for OOS
    }

    /// <summary>SQL restatement of <see cref="ProductUrl.ContributesPriceAnomaly"/> for the other URLs.</summary>
    private static Task<bool> AnyLiveSiblingHasAnomalyAsync(
        OphiDbContext dbContext, ProductUrl productUrl, CancellationToken cancellationToken) =>
        dbContext.ProductUrls.AnyAsync(pu => pu.ProductId == productUrl.ProductId
            && pu.Id != productUrl.Id
            && pu.Status != ProductUrlStatus.Paused
            && pu.HasPriceAnomaly, cancellationToken);

    /// <summary>
    /// Recomputes the product MIN from the live URLs other than <paramref name="pausedUrl"/>, for
    /// the auto-pause paths. A paused URL's price is frozen (the dispatcher skips it), so left in
    /// place it would hold the product price until a sibling's next scrape, or forever on a
    /// single-URL product. No <see cref="PriceUpdatedEvent"/> follows: the paused URL's price was
    /// not scraped now, and on the suspicious path it is not trusted.
    /// </summary>
    private static async Task ReaggregateWithoutAsync(
        OphiDbContext dbContext, Product product, ProductUrl pausedUrl, CancellationToken cancellationToken)
    {
        var livePrices = await dbContext.ProductUrls
            .Where(pu => pu.ProductId == product.Id
                && pu.Id != pausedUrl.Id
                && pu.Status != ProductUrlStatus.Paused
                && pu.CurrentPrice != null)
            .Select(pu => new { Price = pu.CurrentPrice!.Value, pu.Currency })
            .ToListAsync(cancellationToken);

        ProductPriceAggregator.ApplyLiveAggregate(
            product, livePrices.Select(u => new ProductPriceAggregator.UrlPrice(u.Price, u.Currency)).ToList());
    }

    private static async Task HandleFailure(
        ScrapingResult result,
        ProductUrl productUrl,
        Product product,
        Stopwatch stopwatch,
        WorkerSettings settings,
        string storeDomain,
        OphiDbContext dbContext,
        IWebhookDispatchService webhookDispatchService,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var errorMessage = result.ErrorCategory switch
        {
            ScrapeErrorCategory.NotFound => $"Page not found (HTTP {result.HttpStatusCode})",
            ScrapeErrorCategory.Forbidden => "Access blocked (HTTP 403)",
            ScrapeErrorCategory.RateLimited => "Rate limited (HTTP 429) — will retry",
            ScrapeErrorCategory.ServerError => $"Server error (HTTP {result.HttpStatusCode})",
            ScrapeErrorCategory.NetworkError => $"Network error: {result.Error}",
            ScrapeErrorCategory.AntiBot => "Blocked by anti-bot protection",
            ScrapeErrorCategory.ParseError => result.Error ?? "Could not extract price from page",
            _ => result.Error
        };

        // Don't budget rate-limit retries against the auto-pause count.
        if (result.ErrorCategory == ScrapeErrorCategory.RateLimited)
        {
            productUrl.RecordTransientFailure(errorMessage, now);
        }
        else
        {
            productUrl.RecordFailure(errorMessage, now);
        }

        dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildFailureLog(
            product.Id, productUrl.Id, productUrl.LastError,
            stopwatch.ElapsedMilliseconds, storeDomain));

        // User-configurable failure threshold overrides global setting
        var maxFailures = product.User.AutoPauseAfterFailures ?? settings.MaxFailuresBeforeError;

        var atFailureThreshold = productUrl.FailureCount >= maxFailures;

        // Product status is reconciled on EVERY failing check that is at the threshold, independent
        // of the notification latch below. Marking Error is idempotent state, not a once-per-streak
        // event, and coupling the two reintroduced the bug the latch was added to fix: a user who
        // RAISES AutoPauseAfterFailures mid-streak (3 -> 6) has already tripped the latch, so gating
        // on it left the product Active with every URL dead.
        if (atFailureThreshold)
        {
            // Current URL's count is already at threshold in memory (not yet saved to DB).
            // Check if all OTHER URLs have also reached the threshold.
            var otherUrlsAllFailing = await dbContext.ProductUrls
                .Where(pu => pu.ProductId == product.Id && pu.Id != productUrl.Id)
                .AllAsync(pu => pu.FailureCount >= maxFailures, cancellationToken);

            if (otherUrlsAllFailing)
            {
                product.MarkAsError();
            }
        }

        // A challenge page is the site refusing THIS host; the next attempt from the same egress gets
        // the same challenge, so the retries between here and forever are all guaranteed failures.
        // That matters because the product-level guard above only fires when EVERY URL is failing:
        // a blocked URL sharing a product with a healthy one keeps the product Active, so the
        // dispatcher kept re-scraping it every cycle — a Chromium launch per cycle for stores with
        // RequiresJavaScript. Pausing the URL is what actually stops that. Deliberately category-
        // gated: RateLimited/NetworkError/ServerError may clear on their own and keep the old
        // behaviour. Resume() is the way back for an operator whose network changes. See issue #136.
        //
        // Guarded on Status so the pause is a one-time transition, and it consumes the failure latch
        // so the user gets this notification instead of the generic one rather than both. It cannot
        // ride `reachedFailureThreshold` below: a URL that already failed its way past the threshold
        // on another category has FailureNotified set, and would then be paused in silence.
        var blockedFromThisHost = result.ErrorCategory == ScrapeErrorCategory.AntiBot;

        if (atFailureThreshold && blockedFromThisHost && productUrl.Status != ProductUrlStatus.Paused)
        {
            productUrl.Pause();
            productUrl.MarkFailureNotified();
            // The pause takes this URL's anomaly (from an earlier scrape) out of the product flag,
            // and its price out of the product MIN.
            product.RecomputePriceAnomaly(
                productUrl, await AnyLiveSiblingHasAnomalyAsync(dbContext, productUrl, cancellationToken));
            await ReaggregateWithoutAsync(dbContext, product, productUrl, cancellationToken);

            logger.LogWarning(
                "ProductUrl {ProductUrlId} paused after {Count} anti-bot blocks from this host",
                productUrl.Id, productUrl.FailureCount);

            dbContext.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = product.UserId,
                ProductId = product.Id,
                Title = Notification.BuildTitle("URL paused", product.Name),
                Message = $"Paused after {productUrl.FailureCount} attempts: {productUrl.LastError}. "
                    + "This store is refusing automated requests from this server's network, which "
                    + "retrying will not change. Resume the URL if that network changes.",
                Type = NotificationType.UrlHealth
            });
        }

        // Notify once per failure streak, latched on the URL. Comparing with '==' meant that lowering
        // the auto-pause threshold below a URL's existing FailureCount skipped this branch forever
        // (6, 7, 8… never equals 3), so the URL failed silently with no notification or webhook.
        // '>=' plus the latch keeps the once-per-streak behavior without that hole; the latch is
        // cleared whenever the streak ends (see ProductUrl.RecordSuccessfulScrape / MarkOutOfStock).
        var reachedFailureThreshold = atFailureThreshold && !productUrl.FailureNotified;

        if (reachedFailureThreshold)
        {
            productUrl.MarkFailureNotified();

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = product.UserId,
                ProductId = product.Id,
                Title = Notification.BuildTitle("Scrape error", product.Name),
                Message = $"Failed to check price after {productUrl.FailureCount} attempts. Last error: {productUrl.LastError}",
                Type = NotificationType.ScrapeError
            };
            dbContext.Notifications.Add(notification);

            logger.LogWarning("ProductUrl {ProductUrlId} reached max failures ({Count}). Last error: {Error}",
                productUrl.Id, productUrl.FailureCount, productUrl.LastError);
        }
        else
        {
            logger.LogWarning("Failed to get price for {ProductName} URL {ProductUrlId} (Attempt {Count}/{Max}). Last error: {Error}",
                product.Name, productUrl.Id, productUrl.FailureCount, maxFailures, productUrl.LastError);
        }

        // Persist state before any external dispatch to avoid data loss on crash
        await dbContext.SaveChangesAsync(cancellationToken);

        // Dispatch scrape_failed webhook after DB save (non-critical). Reuses the latch decision
        // computed above — re-testing the count here would miss, since the latch is now set.
        if (reachedFailureThreshold)
        {
            try
            {
                var payload = new WebhookPayload(
                    ProductId: product.Id,
                    ProductName: product.Name,
                    ProductUrl: productUrl.Url,
                    OldPrice: null,
                    NewPrice: product.CurrentPrice,
                    Currency: product.Currency,
                    Timestamp: now
                );
                await webhookDispatchService.DispatchAsync(WebhookEvents.ScrapeFailed, product.UserId, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispatch scrape_failed webhook for product {ProductId}", product.Id);
            }
        }
    }
}
