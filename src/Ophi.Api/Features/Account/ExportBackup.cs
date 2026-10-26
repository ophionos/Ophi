using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Account;

/// <summary>
/// Downloads the caller's whole account as one JSON file (B-1a). Credentials are never written —
/// see <see cref="BackupBundle.ExcludedItems"/>. The counterpart is <see cref="ImportBackup"/>.
/// </summary>
public static class ExportBackup
{
    public record Query(Guid UserId);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<BackupBundle> Handle(Query query, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == query.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            var tags = await dbContext.Tags.AsNoTracking()
                .Where(t => t.UserId == query.UserId).OrderBy(t => t.Name)
                .Select(t => new BackupTag(t.Id, t.Name, t.Color, t.Weight))
                .ToListAsync(cancellationToken);

            var groups = await dbContext.ComparisonGroups.AsNoTracking()
                .Where(g => g.UserId == query.UserId).OrderBy(g => g.Name)
                .Select(g => new BackupComparisonGroup(g.Id, g.Name, g.Description))
                .ToListAsync(cancellationToken);

            var stores = await dbContext.StoreConfigurations.AsNoTracking()
                .Where(s => s.UserId == query.UserId).OrderBy(s => s.StoreId)
                .Select(s => new BackupStore(s.StoreId, s.Name, s.DomainPatternsJson, s.SelectorsJson,
                    s.PriceLocale, s.RequiresJavaScript, s.CurrencyOverride, s.AffiliateParamName,
                    s.AffiliateTag, s.CustomUserAgent))
                .ToListAsync(cancellationToken);

            var shippingRates = await dbContext.ShippingRates.AsNoTracking()
                .Where(r => r.UserId == query.UserId).OrderBy(r => r.Host)
                .Select(r => new BackupShippingRate(r.Host, r.Amount, r.Currency, r.FreeAbove))
                .ToListAsync(cancellationToken);

            var baskets = (await dbContext.Baskets.AsNoTracking()
                    .Where(b => b.UserId == query.UserId)
                    .Select(b => new
                    {
                        b.Name,
                        b.Description,
                        Items = b.Items.OrderBy(i => i.AddedAt).Select(i => new BackupBasketItem(i.ProductId, i.Quantity)).ToList(),
                        Alerts = dbContext.BasketAlerts.Where(a => a.BasketId == b.Id).OrderBy(a => a.CreatedAt)
                            .Select(a => new BackupBasketAlert(a.TargetTotal, a.Currency, a.Host, a.IsActive)).ToList()
                    })
                    .ToListAsync(cancellationToken))
                .OrderBy(b => b.Name, StringComparer.Ordinal)
                .Select(b => new BackupBasket(b.Name, b.Description, b.Items, b.Alerts))
                .ToList();

            var products = await dbContext.Products.AsNoTracking()
                .Where(p => p.UserId == query.UserId)
                .Include(p => p.ProductUrls)
                .Include(p => p.ProductTags)
                .Include(p => p.Alerts)
                .OrderBy(p => p.Name)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var productIds = products.Select(p => p.Id).ToList();
            var history = (await dbContext.PricePoints.AsNoTracking()
                    .Where(pp => productIds.Contains(pp.ProductId))
                    .Select(pp => new { pp.ProductId, pp.ProductUrlId, pp.Price, pp.Currency, pp.RecordedAt })
                    .ToListAsync(cancellationToken))
                .OrderBy(pp => pp.RecordedAt)
                .ToLookup(pp => pp.ProductId);

            var bundleProducts = products.Select(p => new BackupProduct(
                p.Name,
                p.ImageUrl,
                p.CurrentPrice,
                p.PreviousPrice,
                p.Currency,
                p.Status.ToApiString(),
                p.IsFavourite,
                p.CheckIntervalMinutes,
                p.CustomFields.Select(f => new BackupCustomField(f.Name, f.Value)).ToList(),
                p.ProductTags.Select(pt => pt.TagId).ToList(),
                p.ComparisonGroupId,
                p.ProductUrls.OrderBy(u => u.CreatedAt).Select(u => new BackupProductUrl(
                    u.Id, u.Url, u.StoreId, u.CurrentPrice, u.Currency, u.LastCheckedAt,
                    u.Status.ToApiString(), u.IsOutOfStock, u.Selector, u.SelectorType.ToApiString())).ToList(),
                history[p.Id].Select(pp => new BackupPricePoint(pp.ProductUrlId, pp.Price, pp.Currency, pp.RecordedAt)).ToList(),
                p.Alerts.OrderBy(a => a.CreatedAt).Select(a => new BackupAlert(
                    a.Condition.ToApiString(), a.TargetPrice, a.ReferencePrice, a.Currency, a.IsActive,
                    a.LastTriggeredAt, a.TriggerCount)).ToList(),
                p.Id
            )).ToList();

            logger.LogInformation("Backup exported for user {UserId}: {Products} products", query.UserId, bundleProducts.Count);

            return new BackupBundle(
                BackupBundle.FormatName,
                BackupBundle.CurrentVersion,
                timeProvider.GetUtcNow().UtcDateTime,
                BackupBundle.ExcludedItems,
                new BackupSettings(
                    user.AffiliatesEnabled,
                    user.DefaultCheckIntervalMinutes,
                    user.PageFetchDelaySeconds,
                    user.ScrapeCacheTtlMinutes,
                    user.AnomalyThresholdPercent,
                    user.AutoPauseAfterFailures,
                    user.EmailNotificationsEnabled,
                    user.DisplayCurrency,
                    user.ShowLandedPrices),
                tags,
                groups,
                stores,
                bundleProducts,
                shippingRates,
                baskets);
        }
    }

    public static void MapExportBackupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/account/backup", async (IMessageBus bus, HttpContext context, TimeProvider time) =>
        {
            var bundle = await bus.InvokeAsync<BackupBundle>(new Query(context.User.GetUserId()));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, BackupBundle.JsonOptions);
            var fileName = $"ophi-backup-{time.GetUtcNow():yyyy-MM-dd}.json";
            return Results.File(bytes, "application/json", fileName);
        })
        .WithName("ExportBackup")
        .WithTags("Account")
        .WithSummary("Download a backup of the whole account")
        .WithDescription("One JSON file: settings, tags, comparison groups, stores, products with URLs, " +
                         "full price history, alerts, shipping rates and baskets. Credentials (password, API keys, notification " +
                         "channel URLs/ids, outbound webhooks) are never included — see its 'excluded' list.")
        .Produces(200, contentType: "application/json")
        .RequireAuthorization();
}
