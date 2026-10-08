using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Alerts;

public static class GetAlerts
{
    public record Query(Guid UserId);

    public record Response(List<Dto> Items);

    /// <param name="Currency">
    /// Denomination of <paramref name="TargetPrice"/>. Not always the product's current currency —
    /// format the target with this one.
    /// </param>
    /// <param name="ProductCurrency">Denomination of <paramref name="CurrentPrice"/>.</param>
    /// <param name="HasCurrencyMismatch">
    /// True when the two currencies differ and the alert is therefore dormant.
    /// </param>
    public record Dto(
        Guid Id,
        Guid ProductId,
        string ProductName,
        decimal? CurrentPrice,
        decimal TargetPrice,
        string Condition,
        bool Active,
        DateTime? LastTriggered,
        string Currency,
        string ProductCurrency,
        bool HasCurrencyMismatch
    );

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching alerts for user {UserId}", request.UserId);

            // Projected in two steps rather than one: the dormancy rule lives in
            // Alert.HasCurrencyMismatch so there is a single definition of it, and that method has no
            // SQL translation. Pull the columns the DTO needs, then apply the rule in memory —
            // alerts are capped per user (AlertSettings.MaxAlertsPerUser), so the set is small.
            var rows = await dbContext.Alerts
                .AsNoTracking()
                .Where(a => a.UserId == request.UserId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.ProductId,
                    ProductName = a.Product.Name,
                    a.Product.CurrentPrice,
                    ProductCurrency = a.Product.Currency,
                    a.TargetPrice,
                    a.Currency,
                    a.Condition,
                    a.IsActive,
                    a.LastTriggeredAt
                })
                .ToListAsync(cancellationToken);

            var items = rows
                .Select(r => new Dto(
                    r.Id,
                    r.ProductId,
                    r.ProductName,
                    r.CurrentPrice,
                    r.TargetPrice,
                    r.Condition.ToApiString(),
                    r.IsActive,
                    r.LastTriggeredAt,
                    r.Currency,
                    r.ProductCurrency,
                    Alert.IsCurrencyMismatch(r.Currency, r.Condition, r.ProductCurrency)
                ))
                .ToList();

            logger.LogDebug("Returning {Count} alerts for user {UserId}", items.Count, request.UserId);
            return new Response(items);
        }
    }

    public static void MapGetAlertsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/alerts", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetAlerts")
        .WithTags("Alerts")
        .WithSummary("List alerts")
        .WithDescription("Returns all alerts for the current user. Includes the alert condition, target price, trigger status, and associated product details.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
