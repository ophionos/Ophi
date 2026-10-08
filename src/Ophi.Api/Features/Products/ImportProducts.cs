using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Extensions;
using static Ophi.Api.Common.Validators.ProductValidationRules;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class ImportProducts
{
    public static void MapImportProductsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/import", async (IMessageBus bus, HttpContext context, CancellationToken ct) =>
        {
            var userId = context.User.GetUserId();

            if (!context.Request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data with a CSV file." });

            var form = await context.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded. Include a CSV file with field name 'file'." });

            List<ImportRow> rows;
            try
            {
                rows = await ParseCsvAsync(file.OpenReadStream(), ct);
            }
            catch (MissingUrlColumnException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (EmptyCsvException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var result = await bus.InvokeAsync<ImportResponse>(new Command(rows) { UserId = userId }, ct);
            return Results.Ok(result);
        })
        .WithName("ImportProducts")
        .WithTags("Products")
        .WithSummary("Import products from CSV")
        .WithDescription("Bulk import products from a CSV file. Required column: 'url'. Optional columns: 'name', 'target_price', 'tags'. Duplicate URLs are skipped. Enqueues scrape jobs for new products.")
        .Produces<ImportResponse>(200)
        .RequireAuthorization()
        .DisableAntiforgery();
    }

    public record ImportResponse(int Added, int Skipped, List<string> Errors);

    public record ImportRow(int LineNumber, string Url, string? Name, decimal? TargetPrice, string? Tags);

    public record Command(IReadOnlyList<ImportRow> Rows)
    {
        public Guid UserId { get; init; }
    }

    internal sealed class MissingUrlColumnException(string message) : Exception(message);
    internal sealed class EmptyCsvException(string message) : Exception(message);

    /// <summary>
    /// Reads a CSV stream and returns the rows. Header names are case-insensitive and trimmed.
    /// The <c>url</c> column is required; <c>name</c>, <c>target_price</c>, and <c>tags</c> are optional.
    /// Throws <see cref="EmptyCsvException"/> when there is no header line and
    /// <see cref="MissingUrlColumnException"/> when the header has no <c>url</c> column.
    /// </summary>
    internal static async Task<List<ImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
            throw new EmptyCsvException("CSV file is empty.");

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        var normalized = headers.Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (!normalized.Contains("url"))
            throw new MissingUrlColumnException("CSV must contain a 'url' column.");

        var rows = new List<ImportRow>();
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            // CsvReader.Parser.Row is 1-based and includes the header row, matching the
            // line-number reporting users expect when looking at their CSV.
            var lineNumber = csv.Parser.Row;
            var url = csv.GetField("url")?.Trim() ?? "";

            var name = TryGetTrimmedField(csv, "name");
            var tags = TryGetTrimmedField(csv, "tags");

            decimal? targetPrice = null;
            var priceText = TryGetTrimmedField(csv, "target_price");
            if (!string.IsNullOrEmpty(priceText) &&
                decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var p))
            {
                targetPrice = p;
            }

            rows.Add(new ImportRow(lineNumber, url, name, targetPrice, tags));
        }

        return rows;
    }

    private static string? TryGetTrimmedField(CsvReader csv, string name)
    {
        if (!csv.HeaderRecord!.Any(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return null;
        var value = csv.GetField(name);
        return string.IsNullOrEmpty(value) ? null : value.Trim();
    }

    public class Handler(OphiDbContext dbContext, IMessageBus messageBus, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        public async Task<ImportResponse> Handle(Command command, CancellationToken cancellationToken)
        {
            var existingUrls = await dbContext.ProductUrls
                .Where(pu => pu.Product.UserId == command.UserId)
                .Select(pu => pu.Url)
                .ToHashSetAsync(cancellationToken);

            var existingTags = await dbContext.Tags
                .Where(t => t.UserId == command.UserId)
                .ToDictionaryAsync(t => t.Name.ToLowerInvariant(), t => t.Id, cancellationToken);

            // The per-user active-alert cap holds here as in CreateAlert and ImportBackup: targets
            // beyond it are imported as paused alerts, not dropped.
            var activeAlerts = await dbContext.Alerts.CountAsync(a => a.UserId == command.UserId && a.IsActive, cancellationToken);
            var maxAlerts = alertSettings.Value.MaxAlertsPerUser;
            var alertsPaused = 0;

            var added = 0;
            var skipped = 0;
            var errors = new List<string>();
            var scrapeQueue = new List<Guid>();

            foreach (var row in command.Rows)
            {
                if (string.IsNullOrEmpty(row.Url))
                {
                    errors.Add($"Line {row.LineNumber}: empty URL");
                    continue;
                }

                if (!IsValidHttpUrl(row.Url))
                {
                    errors.Add($"Line {row.LineNumber}: invalid URL '{row.Url}'");
                    continue;
                }

                if (existingUrls.Contains(row.Url))
                {
                    skipped++;
                    continue;
                }

                // Every row is saved in one SaveChanges, so a value over its varchar bound would
                // fail the whole import on Postgres (SQLite ignores the bound). Reject the row instead.
                var tagNames = string.IsNullOrEmpty(row.Tags)
                    ? []
                    : row.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                if (OverColumnLimit(row, tagNames) is { } limitError)
                {
                    errors.Add($"Line {row.LineNumber}: {limitError}");
                    continue;
                }

                if (row.TargetPrice <= 0)
                {
                    errors.Add($"Line {row.LineNumber}: target_price must be greater than 0");
                    continue;
                }

                var name = string.IsNullOrEmpty(row.Name) ? "Loading..." : row.Name;

                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    UserId = command.UserId,
                    Name = name,
                    Currency = "USD",
                    Status = name == "Loading..." ? ProductStatus.Pending : ProductStatus.Active
                };

                var productUrl = new ProductUrl
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Url = row.Url,
                    Currency = "USD",
                    SelectorType = SelectorType.Auto
                };

                dbContext.Products.Add(product);
                dbContext.ProductUrls.Add(productUrl);

                if (row.TargetPrice.HasValue)
                {
                    var active = activeAlerts < maxAlerts;
                    if (active) activeAlerts++;
                    else alertsPaused++;

                    dbContext.Alerts.Add(new Alert
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        UserId = command.UserId,
                        Condition = AlertCondition.Below,
                        TargetPrice = row.TargetPrice.Value,
                        IsActive = active
                    });
                }

                foreach (var tagName in tagNames)
                {
                    var key = tagName.ToLowerInvariant();
                    if (!existingTags.TryGetValue(key, out var tagId))
                    {
                        var newTag = new Tag
                        {
                            Id = Guid.NewGuid(),
                            UserId = command.UserId,
                            Name = tagName
                        };
                        dbContext.Tags.Add(newTag);
                        tagId = newTag.Id;
                        existingTags[key] = tagId;
                    }

                    dbContext.ProductTags.Add(new ProductTag
                    {
                        ProductId = product.Id,
                        TagId = tagId
                    });
                }

                existingUrls.Add(row.Url);
                added++;

                scrapeQueue.Add(productUrl.Id);
            }

            if (alertsPaused > 0)
                errors.Add($"{alertsPaused} alert(s) were imported paused: the account reached its limit of {maxAlerts} active alerts.");

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                foreach (var productUrlId in scrapeQueue)
                    await messageBus.PublishAsync(new ScrapeProductUrlCommand(productUrlId));
            }

            logger.LogInformation("Product import for user {UserId}: {Added} added, {Skipped} skipped, {Errors} errors",
                command.UserId, added, skipped, errors.Count);

            return new ImportResponse(added, skipped, errors);
        }

        private static string? OverColumnLimit(ImportRow row, string[] tagNames)
        {
            if (row.Url.Length > ProductUrlConfiguration.UrlMaxLength)
                return $"URL longer than {ProductUrlConfiguration.UrlMaxLength} characters";
            if (row.Name?.Length > ProductConfiguration.NameMaxLength)
                return $"name longer than {ProductConfiguration.NameMaxLength} characters";
            if (tagNames.FirstOrDefault(t => t.Length > TagConfiguration.NameMaxLength) is { } longTag)
                return $"tag '{longTag[..20]}…' longer than {TagConfiguration.NameMaxLength} characters";
            return null;
        }
    }
}
