using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Alerts;

public static class CreateAlert
{
    public record Request(Guid ProductId, decimal TargetPrice, string Condition);

    public record Command(Guid ProductId, decimal TargetPrice, string Condition)
    {
        public Guid UserId { get; init; }
    }

    /// <remarks>
    /// Deliberately the same field set as <see cref="GetAlerts.Dto"/> plus <c>CreatedAt</c>: the web
    /// client models both endpoints with one <c>Alert</c> type, so anything omitted here is a field
    /// that type declares and never receives. Nothing costs an extra query — the handler already
    /// loads the product to authorize the request.
    /// </remarks>
    /// <param name="Currency">Denomination of <paramref name="TargetPrice"/>.</param>
    /// <param name="ProductCurrency">Denomination of <paramref name="CurrentPrice"/>.</param>
    /// <param name="HasCurrencyMismatch">
    /// True when the two currencies differ and the alert is therefore dormant. Always false on
    /// creation, since the target is stamped with the product's own currency — present so the
    /// client can treat a created alert exactly like a listed one.
    /// </param>
    public record Response(
        Guid Id,
        Guid ProductId,
        string ProductName,
        decimal? CurrentPrice,
        decimal TargetPrice,
        string Condition,
        bool Active,
        DateTime? LastTriggered,
        DateTime CreatedAt,
        string Currency,
        string ProductCurrency,
        bool HasCurrencyMismatch
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ProductId)
                .NotEmpty();

            RuleFor(x => x.TargetPrice)
                .GreaterThan(0);

            // A price is > 0, so a drop of 100 % or more can never happen.
            RuleFor(x => x.TargetPrice)
                .LessThan(100)
                .When(x => x.Condition == "percentDrop")
                .WithMessage("A percentage drop must be less than 100");

            RuleFor(x => x.Condition)
                .NotEmpty()
                .Must(c => c is "below" or "above" or "percentDrop")
                .WithMessage("Condition must be 'below', 'above', or 'percentDrop'");
        }
    }

    public class Handler(OphiDbContext dbContext, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        private readonly AlertSettings _alertSettings = alertSettings.Value;

        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Run sequentially: EF Core's DbContext does not allow concurrent operations on the
            // same instance, so these queries cannot share one scoped context via Task.WhenAll.
            var product = await dbContext.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");
            var userAlertCount = await dbContext.Alerts
                .CountAsync(a => a.UserId == request.UserId && a.IsActive, cancellationToken);

            if (userAlertCount >= _alertSettings.MaxAlertsPerUser)
            {
                throw new ApiException(
                    $"Maximum number of active alerts ({_alertSettings.MaxAlertsPerUser}) reached.", 422, "MaxAlertsReached");
            }

            var condition = request.Condition switch
            {
                "below" => AlertCondition.Below,
                "above" => AlertCondition.Above,
                _ => AlertCondition.PercentDrop
            };

            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                UserId = request.UserId,
                TargetPrice = request.TargetPrice,
                ReferencePrice = product.CurrentPrice ?? request.TargetPrice,
                // The user picked this number while looking at the product priced in this currency,
                // so that is what the target means. Pinning it here is what lets a later re-anchor
                // be detected as a mismatch instead of silently re-denominating the target.
                Currency = product.Currency,
                Condition = condition,
                IsActive = true
            };

            dbContext.Alerts.Add(alert);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Alert {AlertId} created for product {ProductId}", alert.Id, request.ProductId);

            return new Response(
                alert.Id,
                alert.ProductId,
                product.Name,
                product.CurrentPrice,
                alert.TargetPrice,
                request.Condition,
                alert.IsActive,
                alert.LastTriggeredAt,
                alert.CreatedAt,
                alert.Currency,
                product.Currency,
                alert.HasCurrencyMismatch(product.Currency)
            );
        }
    }

    public static void MapCreateAlertEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/alerts", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.ProductId, request.TargetPrice, request.Condition)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/alerts/{result.Id}", result);
        })
        .WithName("CreateAlert")
        .WithTags("Alerts")
        .WithSummary("Create an alert")
        .WithDescription("Creates a price alert for a product. Conditions: 'below' (price drops below target), 'above' (price rises above target), or 'percentDrop' (price drops by a percentage). Each user can have at most Alerts:MaxAlertsPerUser active alerts (default 100); over the cap the request fails with 422. Notifications go to the user's enabled channels, with a cooldown to prevent spam.")
        .Produces<Response>(201)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.AlertCreation);
}
