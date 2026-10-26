using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class CreateBasketAlert
{
    public record Request(decimal TargetTotal, string? Host);

    public record Command(Guid BasketId, decimal TargetTotal, string? Host)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.TargetTotal).GreaterThan(0);
            RuleFor(x => x.Host).MaximumLength(ShippingRateConfiguration.HostMaxLength);
        }
    }

    public class Handler(OphiDbContext dbContext, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        private readonly AlertSettings _alertSettings = alertSettings.Value;

        public async Task<BasketAlertDto> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!await dbContext.Baskets.AnyAsync(b => b.Id == request.BasketId && b.UserId == request.UserId, cancellationToken))
                throw new NotFoundException("Basket not found");

            var activeCount = await dbContext.BasketAlerts.CountAsync(a => a.UserId == request.UserId && a.IsActive, cancellationToken);
            if (activeCount >= _alertSettings.MaxAlertsPerUser) throw BasketAlertLimit.Reached(_alertSettings.MaxAlertsPerUser);

            // A pasted product URL names its site too.
            var host = string.IsNullOrWhiteSpace(request.Host)
                ? null
                : SiteHost.Of(request.Host.Trim()) ?? SiteHost.Normalize(request.Host.Trim());
            var totals = await BasketTotalsReader.ComputeAsync(dbContext, request.BasketId, request.UserId, cancellationToken);
            // The target means a total the user saw, so the alert takes that total's currency. With no
            // total to watch (no complete store, mixed currencies) there is nothing to denominate it in.
            var watched = new BasketAlert { Host = host }.Check(totals)
                ?? throw new ApiException(
                    host is null
                        ? "No store has every item of this basket in one currency yet, so there is no total to watch."
                        : $"{host} has no total for this basket yet: it lacks an item or mixes currencies.",
                    422, "NoWatchedTotal");

            var alert = new BasketAlert
            {
                Id = Guid.NewGuid(),
                BasketId = request.BasketId,
                UserId = request.UserId,
                TargetTotal = request.TargetTotal,
                Currency = watched.Currency,
                Host = host
            };
            dbContext.BasketAlerts.Add(alert);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Basket alert {BasketAlertId} created for basket {BasketId}", alert.Id, request.BasketId);
            return BasketAlertDto.From(alert, totals);
        }
    }

    public static void MapCreateBasketAlertEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/baskets/{id:guid}/alerts", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.TargetTotal, request.Host) { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<BasketAlertDto>(command);
            return Results.Created($"/api/v1/baskets/{id}/alerts/{result.Id}", result);
        })
        .WithName("CreateBasketAlert")
        .WithTags("Baskets")
        .WithSummary("Create a basket alert")
        .WithDescription("Creates an alert that fires when the basket's total is at or under `targetTotal`. `host` watches one store (site): a host such as `amazon.es` or a URL on that site; null watches the best complete store. The alert takes the currency of the watched total; when there is no complete store to watch (or the named store lacks an item or mixes currencies) the request fails with 422 NoWatchedTotal. Each user can have at most Alerts:MaxAlertsPerUser active basket alerts, counted apart from product alerts (422 MaxAlertsReached). The check runs on the next price change of an item, not on a quantity or shipping-rate change. Returns 404 for a basket that belongs to another user.")
        .Produces<BasketAlertDto>(201)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.AlertCreation);
}
