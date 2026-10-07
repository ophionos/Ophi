using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Features.Settings;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;
using static Ophi.Api.Common.Validators.ProductValidationRules;

namespace Ophi.Api.Features.Account;

/// <summary>
/// Restores an <see cref="ExportBackup"/> file into the caller's account (B-1b).
/// <para>
/// <b>Merge only</b> — never deletes or overwrites: tags and comparison groups are matched by name
/// and reused, an existing store config with the same <c>storeId</c> is kept as it is, and a product
/// with any already-tracked URL is skipped whole, and settings are restored only into an account
/// with no products yet. Everything created gets a new ID. Active alerts
/// still count against <see cref="AlertSettings.MaxAlertsPerUser"/>; any beyond it arrive paused.
/// One transaction: a failure leaves the account untouched.
/// </para>
/// </summary>
public static class ImportBackup
{
    public const long MaxFileBytes = 25 * 1024 * 1024;

    public record Command(BackupBundle Bundle)
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        bool SettingsRestored,
        int TagsAdded,
        int ComparisonGroupsAdded,
        int StoresAdded,
        int StoresKept,
        int ProductsAdded,
        int ProductsSkipped,
        int PricePointsAdded,
        int AlertsAdded,
        int AlertsPaused,
        IReadOnlyList<string> Warnings);

    public class Handler(OphiDbContext dbContext, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            var bundle = command.Bundle;
            if (bundle.Format != BackupBundle.FormatName || bundle.Version != BackupBundle.CurrentVersion)
            {
                throw new ApiException(
                    $"Not an Ophi backup this server can read (format '{bundle.Format}', version {bundle.Version}).",
                    400, "UnsupportedBackup");
            }

            // Deserialization leaves missing sections null; refuse rather than half-import.
            if (bundle.Settings is null || bundle.Tags is null || bundle.ComparisonGroups is null
                || bundle.Stores is null || bundle.Products is null
                || bundle.Products.Any(p => p?.Urls is null || p.PriceHistory is null || p.Alerts is null
                                            || p.TagRefs is null || p.CustomFields is null))
            {
                throw new ApiException("The backup file is incomplete or damaged.", 400, "MalformedBackup");
            }

            var userId = command.UserId;
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            var warnings = new List<string>();
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Settings are only restored into an account with no products yet — the migrate-to-a-new-
            // account case. Anywhere else they are the user's current choices, and merge-only means
            // the file never overwrites them.
            var settingsRestored = false;
            if (await dbContext.Products.AnyAsync(p => p.UserId == userId, cancellationToken))
                warnings.Add("Settings were left unchanged: they are only restored into an account with no products yet.");
            else
                settingsRestored = ApplySettings(user, bundle.Settings, warnings);

            // --- Tags and comparison groups: reuse by name (case-insensitive), else create ---
            var tagIds = new Dictionary<Guid, Guid>();
            var tagsByName = await dbContext.Tags.Where(t => t.UserId == userId)
                .ToDictionaryAsync(t => t.Name.ToLowerInvariant(), t => t.Id, cancellationToken);
            var tagsAdded = 0;
            foreach (var t in bundle.Tags)
            {
                if (string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 50) { warnings.Add($"Tag '{t.Name}' skipped: invalid name"); continue; }
                if (!tagsByName.TryGetValue(t.Name.ToLowerInvariant(), out var id))
                {
                    id = Guid.NewGuid();
                    dbContext.Tags.Add(new Tag { Id = id, UserId = userId, Name = t.Name, Color = t.Color.Length <= 7 ? t.Color : "#3B82F6", Weight = t.Weight });
                    tagsByName[t.Name.ToLowerInvariant()] = id;
                    tagsAdded++;
                }
                tagIds[t.Ref] = id;
            }

            var groupIds = new Dictionary<Guid, Guid>();
            var groupsByName = await dbContext.ComparisonGroups.Where(g => g.UserId == userId)
                .ToDictionaryAsync(g => g.Name.ToLowerInvariant(), g => g.Id, cancellationToken);
            var groupsAdded = 0;
            foreach (var g in bundle.ComparisonGroups)
            {
                if (string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 100) { warnings.Add($"Comparison group '{g.Name}' skipped: invalid name"); continue; }
                if (!groupsByName.TryGetValue(g.Name.ToLowerInvariant(), out var id))
                {
                    id = Guid.NewGuid();
                    dbContext.ComparisonGroups.Add(new ComparisonGroup { Id = id, UserId = userId, Name = g.Name, Description = g.Description?.Length > 500 ? g.Description[..500] : g.Description });
                    groupsByName[g.Name.ToLowerInvariant()] = id;
                    groupsAdded++;
                }
                groupIds[g.Ref] = id;
            }

            // --- Stores: create missing, never overwrite an existing config ---
            var storeIds = await dbContext.StoreConfigurations.Where(s => s.UserId == userId)
                .Select(s => s.StoreId).ToListAsync(cancellationToken);
            var knownStores = new HashSet<string>(storeIds, StringComparer.OrdinalIgnoreCase);
            int storesAdded = 0, storesKept = 0;
            foreach (var s in bundle.Stores)
            {
                if (knownStores.Contains(s.StoreId)) { storesKept++; continue; }
                if (string.IsNullOrWhiteSpace(s.StoreId) || s.StoreId.Length > 50 || s.Name.Length > 100
                    || s.DomainPatternsJson.Length > 2000 || s.SelectorsJson.Length > 10000 || s.PriceLocale.Length > 10)
                {
                    warnings.Add($"Store '{s.StoreId}' skipped: invalid configuration");
                    continue;
                }
                dbContext.StoreConfigurations.Add(new StoreConfiguration
                {
                    Id = Guid.NewGuid(), UserId = userId, StoreId = s.StoreId, Name = s.Name,
                    DomainPatternsJson = s.DomainPatternsJson, SelectorsJson = s.SelectorsJson,
                    PriceLocale = s.PriceLocale, RequiresJavaScript = s.RequiresJavaScript,
                    CurrencyOverride = s.CurrencyOverride, AffiliateParamName = s.AffiliateParamName,
                    AffiliateTag = s.AffiliateTag, CustomUserAgent = s.CustomUserAgent
                });
                knownStores.Add(s.StoreId);
                storesAdded++;
            }

            // --- Products ---
            var trackedUrls = await dbContext.ProductUrls.Where(u => u.Product.UserId == userId)
                .Select(u => u.Url).ToHashSetAsync(cancellationToken);
            var activeAlerts = await dbContext.Alerts.CountAsync(a => a.UserId == userId && a.IsActive, cancellationToken);
            var maxAlerts = alertSettings.Value.MaxAlertsPerUser;
            int productsAdded = 0, productsSkipped = 0, pricePointsAdded = 0, alertsAdded = 0, alertsPaused = 0;

            foreach (var p in bundle.Products)
            {
                var urls = p.Urls.Where(u => IsValidHttpUrl(u.Url) && u.Url.Length <= 2048)
                    .DistinctBy(u => u.Url).ToList();
                if (urls.Count == 0 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 500)
                {
                    warnings.Add($"Product '{p.Name}' skipped: no valid URL or invalid name");
                    continue;
                }
                if (urls.Any(u => trackedUrls.Contains(u.Url)))
                {
                    productsSkipped++;
                    continue;
                }

                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = p.Name,
                    ImageUrl = p.ImageUrl is { Length: <= 2048 } img && IsValidHttpUrl(img) ? img : null,
                    CurrentPrice = p.CurrentPrice,
                    PreviousPrice = p.PreviousPrice,
                    Currency = NormalizeCurrency(p.Currency),
                    Status = ParseEnum(p.Status, ProductStatus.Active),
                    IsFavourite = p.IsFavourite,
                    CheckIntervalMinutes = p.CheckIntervalMinutes,
                    ComparisonGroupId = p.ComparisonGroupRef is { } gr && groupIds.TryGetValue(gr, out var gid) ? gid : null,
                    CustomFields = p.CustomFields.Select(f => new CustomField(f.Name, f.Value)).ToList()
                };
                dbContext.Products.Add(product);

                var urlIds = new Dictionary<Guid, Guid>();
                foreach (var u in urls)
                {
                    var urlId = Guid.NewGuid();
                    urlIds[u.Ref] = urlId;
                    trackedUrls.Add(u.Url);
                    dbContext.ProductUrls.Add(new ProductUrl
                    {
                        Id = urlId,
                        ProductId = product.Id,
                        Url = u.Url,
                        StoreId = u.StoreId is { Length: <= 100 } sid ? sid : null,
                        CurrentPrice = u.CurrentPrice,
                        Currency = NormalizeCurrency(u.Currency),
                        // Kept so the dispatcher schedules these normally instead of all at once.
                        LastCheckedAt = u.LastCheckedAt,
                        Status = ParseEnum(u.Status, ProductUrlStatus.Active),
                        IsOutOfStock = u.IsOutOfStock,
                        Selector = u.Selector is { Length: <= 500 } sel ? sel : null,
                        SelectorType = ParseEnum(u.SelectorType, SelectorType.Auto)
                    });
                }

                foreach (var tagRef in p.TagRefs.Distinct())
                {
                    if (tagIds.TryGetValue(tagRef, out var tagId))
                        dbContext.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tagId });
                }

                foreach (var pp in p.PriceHistory)
                {
                    if (pp.Price <= 0) continue; // same invariant as the parser: a price is > 0
                    dbContext.PricePoints.Add(new PricePoint
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        ProductUrlId = pp.UrlRef is { } r && urlIds.TryGetValue(r, out var mapped) ? mapped : null,
                        Price = pp.Price,
                        Currency = NormalizeCurrency(pp.Currency),
                        RecordedAt = DateTime.SpecifyKind(pp.RecordedAt, DateTimeKind.Utc)
                    });
                    pricePointsAdded++;
                }

                foreach (var a in p.Alerts)
                {
                    if (!Enum.TryParse<AlertCondition>(a.Condition, ignoreCase: true, out var condition) || a.TargetPrice <= 0)
                    {
                        warnings.Add($"An alert on '{p.Name}' was skipped: unreadable condition or target");
                        continue;
                    }
                    var active = a.Active;
                    if (active && activeAlerts >= maxAlerts)
                    {
                        active = false;
                        alertsPaused++;
                    }
                    if (active) activeAlerts++;

                    dbContext.Alerts.Add(new Alert
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        UserId = userId,
                        TargetPrice = a.TargetPrice,
                        ReferencePrice = a.ReferencePrice,
                        Currency = NormalizeCurrency(a.Currency),
                        Condition = condition,
                        IsActive = active,
                        LastTriggeredAt = a.LastTriggeredAt,
                        TriggerCount = a.TriggerCount
                    });
                    alertsAdded++;
                }

                productsAdded++;
            }

            if (alertsPaused > 0)
            {
                warnings.Add($"{alertsPaused} alert(s) were imported paused: the account reached its limit of {maxAlerts} active alerts.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Backup imported for user {UserId}: {Added} products added, {Skipped} skipped",
                userId, productsAdded, productsSkipped);

            return new Response(settingsRestored, tagsAdded, groupsAdded, storesAdded, storesKept,
                productsAdded, productsSkipped, pricePointsAdded, alertsAdded, alertsPaused, warnings);
        }

        /// <summary>Runs the same validator as <see cref="UpdateSettings"/>, so a file can't set what the UI couldn't.</summary>
        private static bool ApplySettings(User user, BackupSettings s, List<string> warnings)
        {
            var asCommand = new UpdateSettings.Command(
                s.AffiliatesEnabled, s.DefaultCheckIntervalMinutes, s.PageFetchDelaySeconds, s.ScrapeCacheTtlMinutes,
                AnomalyThresholdPercent: s.AnomalyThresholdPercent, AutoPauseAfterFailures: s.AutoPauseAfterFailures,
                EmailNotificationsEnabled: s.EmailNotificationsEnabled, DisplayCurrency: s.DisplayCurrency);
            var validation = new UpdateSettings.Validator().Validate(asCommand);
            if (!validation.IsValid)
            {
                warnings.Add("Settings were not restored: " + string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
                return false;
            }

            user.AffiliatesEnabled = s.AffiliatesEnabled;
            user.DefaultCheckIntervalMinutes = s.DefaultCheckIntervalMinutes;
            user.PageFetchDelaySeconds = s.PageFetchDelaySeconds;
            user.ScrapeCacheTtlMinutes = s.ScrapeCacheTtlMinutes;
            user.AnomalyThresholdPercent = s.AnomalyThresholdPercent;
            user.AutoPauseAfterFailures = s.AutoPauseAfterFailures;
            user.EmailNotificationsEnabled = s.EmailNotificationsEnabled;
            user.DisplayCurrency = string.IsNullOrWhiteSpace(s.DisplayCurrency) ? null : s.DisplayCurrency.ToUpperInvariant();
            return true;
        }

        private static string NormalizeCurrency(string? code) =>
            code is { Length: 3 } ? code.ToUpperInvariant() : "USD";

        private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
            Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
    }

    public static void MapImportBackupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/account/backup", async (IMessageBus bus, HttpContext context, CancellationToken ct) =>
        {
            if (!context.Request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data with the backup file." });

            var form = await context.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded. Include the backup with field name 'file'." });

            BackupBundle? bundle;
            try
            {
                await using var stream = file.OpenReadStream();
                bundle = await JsonSerializer.DeserializeAsync<BackupBundle>(stream, BackupBundle.JsonOptions, ct);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "The file is not a valid Ophi backup (unreadable JSON)." });
            }
            if (bundle is null)
                return Results.BadRequest(new { error = "The file is not a valid Ophi backup." });

            var result = await bus.InvokeAsync<Response>(new Command(bundle) { UserId = context.User.GetUserId() }, ct);
            return Results.Ok(result);
        })
        .WithName("ImportBackup")
        .WithTags("Account")
        .WithSummary("Restore a backup into this account (merge only)")
        .WithDescription("Adds what the file has that the account doesn't: tags and groups are reused by name, " +
                         "existing stores are kept, products whose URL is already tracked are skipped. " +
                         "Settings are restored only into an account with no products yet. Nothing is deleted or overwritten. Max 25 MB.")
        .Produces<Response>(200)
        .WithMetadata(new RequestSizeLimitAttribute(MaxFileBytes))
        .RequireAuthorization()
        .DisableAntiforgery();
}
