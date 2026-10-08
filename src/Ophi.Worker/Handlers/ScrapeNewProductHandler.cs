using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

[WolverineHandler]
[LocalQueue("scraping")]
public static class ScrapeNewProductHandler
{
    public static async Task<PriceUpdatedEvent?> HandleAsync(
        ScrapeProductUrlCommand command,
        OphiDbContext dbContext,
        IScrapingService scrapingService,
        IAutoCreateStoreService autoCreateStoreService,
        IStoreConfigProvider configProvider,
        TimeProvider timeProvider,
        IMessageBus bus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting scrape for product URL {ProductUrlId}", command.ProductUrlId);
        var now = timeProvider.GetUtcNow().UtcDateTime;

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

        if (!command.Force && product.Status != ProductStatus.Pending)
        {
            logger.LogDebug("Product {ProductId} is not pending (status: {Status}), skipping",
                product.Id, product.Status);
            return null;
        }

        var storeDomain = AppMetrics.ExtractStore(productUrl.Url);

        // Ping the user's open streams to refetch once a scrape outcome (success / OOS / failure) is
        // committed. Thin signal; short expiry so a durable-queue replay after a restart is dropped.
        // Not called on early no-op returns or the catch's rethrow (that path retries).
        Task PublishScrapeCompleted() => bus.PublishAsync(
            new LiveUpdate(product.UserId, LiveUpdate.ScrapeCompleted, product.Id),
            new DeliveryOptions { DeliverWithin = TimeSpan.FromSeconds(30) }).AsTask();

        try
        {
            await PageFetchDelayHelper.ApplyAsync(product.User, cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            // captureHtml: true — this is the first-time scrape, so ask the generic path for the
            // rendered HTML to drive auto store-config creation (incl. the Playwright fallback, which
            // otherwise returns null HTML). Recurring scrapes in CheckProductPriceHandler leave it false.
            var result = await scrapingService.ScrapeProductAsync(
                productUrl.Url,
                productUrl.Selector,
                product.UserId,
                captureHtml: true,
                cancellationToken: cancellationToken);
            stopwatch.Stop();

            // Success with price and not out of stock
            if (result is { Success: true, Price: not null } && !result.IsOutOfStock)
            {
                product.Name = result.Name ?? product.Name;
                product.ImageUrl = result.ImageUrl;
                product.MarkActive();

                productUrl.RecordSuccessfulScrape(result.Price.Value, result.Currency, now);
                productUrl.Selector = result.DetectedSelector ?? productUrl.Selector;

                // Recompute across all live URLs instead of assigning this URL's price to the
                // product. On the initial Pending scrape this URL is the only contributor, so the
                // outcome is unchanged (and ApplyAggregate's re-anchor rule adopts a foreign scrape
                // currency over the product's "USD" default). But this handler also runs with
                // Force:true from the manual retry action on an existing multi-URL product, where a
                // direct assignment discarded the MIN across siblings and re-denominated the product
                // from whichever URL happened to be retried.
                var oldProductPrice = product.CurrentPrice;
                // Captured before ApplyAggregate, which may re-anchor the product's currency.
                var oldProductCurrency = product.Currency;

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

                // Auto-create store config if scrape used generic adapter
                if (result.StoreId is null or "generic" && result.FetchedHtml != null)
                {
                    await TryAutoCreateStoreAsync(
                        productUrl.Url, result.FetchedHtml, product.UserId,
                        dbContext, autoCreateStoreService, configProvider, logger, cancellationToken);
                }

                dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildSuccessLog(
                    product.Id, productUrl.Id, result.Price.Value,
                    stopwatch.ElapsedMilliseconds, storeDomain));

                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Successfully scraped product URL {ProductUrlId}: {Name} - {Price} {Currency}",
                    command.ProductUrlId, product.Name, result.Price, result.Currency);

                await PublishScrapeCompleted();

                // Product tier carries the MIN-across-URLs aggregate (alerts, price-changed webhook);
                // URL tier carries what this scrape actually saw (per-URL history). On the initial
                // scrape oldProductPrice is null and the two tiers coincide, as before.
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

            // Out of stock on initial scrape — product exists but is unavailable
            if (result.IsOutOfStock)
            {
                product.Name = result.Name ?? product.Name;
                product.ImageUrl = result.ImageUrl;
                // The forced retry reaches already-priced products; a kept price keeps its currency.
                product.AdoptCurrencyWhileUnpriced(result.Currency);
                product.MarkActive(); // Product exists, just OOS

                productUrl.MarkOutOfStock(now, result.Currency);
                productUrl.Selector = result.DetectedSelector ?? productUrl.Selector;

                // Auto-create store config if applicable
                if (result.StoreId is null or "generic" && result.FetchedHtml != null)
                {
                    await TryAutoCreateStoreAsync(
                        productUrl.Url, result.FetchedHtml, product.UserId,
                        dbContext, autoCreateStoreService, configProvider, logger, cancellationToken);
                }

                dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildSuccessLog(
                    product.Id, productUrl.Id, result.Price,
                    stopwatch.ElapsedMilliseconds, storeDomain, isOutOfStock: true));

                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = product.UserId,
                    ProductId = product.Id,
                    Title = Notification.BuildTitle("Out of stock", product.Name),
                    Message = "Product was found but appears to be out of stock.",
                    Type = NotificationType.OutOfStock
                });

                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Product URL {ProductUrlId} scraped but out of stock: {Name}",
                    command.ProductUrlId, product.Name);

                await PublishScrapeCompleted();
                return null;
            }

            // Failure
            product.MarkAsError();
            productUrl.RecordFailure(result.Error ?? "Failed to extract price", now);

            dbContext.ScrapeLogs.Add(ScrapeOutcomeWriter.BuildFailureLog(
                product.Id, productUrl.Id, result.Error ?? "Failed to extract price",
                stopwatch.ElapsedMilliseconds, storeDomain));

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning("Failed to scrape product URL {ProductUrlId}: {Error}",
                command.ProductUrlId, result.Error);

            await PublishScrapeCompleted();
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scraping product URL {ProductUrlId}", command.ProductUrlId);

            product.MarkAsError();
            productUrl.RecordFailure(ex.Message, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private static async Task TryAutoCreateStoreAsync(
        string url,
        string html,
        Guid userId,
        OphiDbContext dbContext,
        IAutoCreateStoreService autoCreateStoreService,
        IStoreConfigProvider configProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysisResult = await autoCreateStoreService.AnalyzeHtmlAsync(html, url, cancellationToken);
            if (analysisResult == null)
            {
                logger.LogDebug("Auto-store analysis found no viable selectors for {Url}", url);
                return;
            }

            // Check if user already has a store config for this domain
            var existingConfig = await configProvider.GetConfigForUrlAsync(url, userId, cancellationToken);
            if (existingConfig != null && !existingConfig.IsBuiltIn)
            {
                logger.LogDebug("User already has store config for domain {Domain}", analysisResult.Domain);
                return;
            }

            var storeId = $"auto-{analysisResult.Domain.Replace('.', '-')}";

            // Skip if a store with this auto-generated ID already exists (e.g. from a prior auto-creation)
            var alreadyExists = await dbContext.StoreConfigurations
                .AnyAsync(s => s.UserId == userId && s.StoreId == storeId, cancellationToken);
            if (alreadyExists)
            {
                logger.LogDebug("Auto-store '{StoreId}' already exists, skipping creation", storeId);
                return;
            }

            var entity = new StoreConfiguration
            {
                StoreId = storeId,
                Name = analysisResult.StoreName,
                DomainPatternsJson = JsonSerializer.Serialize(new[] { analysisResult.Domain }),
                SelectorsJson = JsonSerializer.Serialize(analysisResult.Selectors),
                UserId = userId,
                IsAutoCreated = true
            };

            dbContext.StoreConfigurations.Add(entity);
            configProvider.InvalidateCache(userId);

            logger.LogInformation("Auto-created store config '{StoreId}' for domain {Domain}",
                storeId, analysisResult.Domain);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to auto-create store config for {Url}", url);
        }
    }
}
