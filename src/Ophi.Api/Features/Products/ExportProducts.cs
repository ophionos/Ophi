using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Features.Products;

public static class ExportProducts
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static void MapExportProductsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products/export", async (string? format, HttpContext context, OphiDbContext dbContext, CancellationToken ct) =>
        {
            var userId = context.User.GetUserId();
            var outputFormat = format?.ToLowerInvariant() ?? "csv";

            var products = await dbContext.Products
                .Where(p => p.UserId == userId && p.Status == ProductStatus.Active)
                .Include(p => p.ProductUrls)
                .Include(p => p.ProductTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.Alerts.Where(a => a.IsActive))
                .OrderBy(p => p.Name)
                .ToListAsync(ct);

            // Get lowest prices from price history
            var productIds = products.Select(p => p.Id).ToList();
            var lowestPrices = await dbContext.PricePoints
                .Where(pp => productIds.Contains(pp.ProductId))
                .GroupBy(pp => pp.ProductId)
                .Select(g => new { ProductId = g.Key, LowestPrice = g.Min(pp => pp.Price) })
                .ToDictionaryAsync(x => x.ProductId, x => x.LowestPrice, ct);

            var rows = products.Select(p => new ExportRow(
                p.Name,
                string.Join(" | ", p.ProductUrls.Select(u => u.Url)),
                p.CurrentPrice,
                lowestPrices.GetValueOrDefault(p.Id),
                p.Alerts.FirstOrDefault()?.TargetPrice,
                p.Currency,
                string.Join(", ", p.ProductTags.Select(pt => pt.Tag.Name)),
                p.ProductUrls.Max(u => u.LastCheckedAt)
            )).ToList();

            if (outputFormat == "json")
            {
                return Results.Json(rows, JsonOptions);
            }

            // CSV
            var sb = new StringBuilder();
            sb.AppendLine("name,url,current_price,lowest_price,target_price,currency,tags,last_checked");
            foreach (var row in rows)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{Escape(row.Name)},{Escape(row.Url)},{row.CurrentPrice},{row.LowestPrice},{row.TargetPrice},{row.Currency},{Escape(row.Tags)},{row.LastChecked:O}");
            }

            return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
        })
        .WithName("ExportProducts")
        .WithTags("Products")
        .WithSummary("Export products as CSV or JSON")
        .WithDescription("Exports all active products. Pass ?format=json for JSON, defaults to CSV.")
        .Produces<string>(200)
        .RequireAuthorization();
    }

    internal record ExportRow(
        string Name,
        string Url,
        decimal? CurrentPrice,
        decimal? LowestPrice,
        decimal? TargetPrice,
        string Currency,
        string Tags,
        DateTime? LastChecked);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
