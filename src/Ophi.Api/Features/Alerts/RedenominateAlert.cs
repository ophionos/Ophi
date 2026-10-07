using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Alerts;

/// <summary>
/// Recovery path for a dormant alert — one whose <c>Currency</c> no longer matches its product's
/// because the product re-anchored onto another currency. Before this, the only way out was to
/// delete the alert and create a new one, which is not something a user would guess at and loses
/// the alert's trigger history.
/// <para>
/// Deliberately not a general <c>UpdateAlert</c>: it refuses any alert that is not dormant, so it
/// cannot be used to edit a live alert's target by a side door.
/// </para>
/// </summary>
public static class RedenominateAlert
{
    /// <param name="ExpectedCurrency">
    /// The currency the user believed they were entering <paramref name="TargetPrice"/> in — that
    /// is, what the page displayed. Checked against the product's actual currency at handle time so
    /// a re-anchor between page load and submit is rejected rather than silently stamping the
    /// number with a denomination the user never saw.
    /// </param>
    public record Request(decimal TargetPrice, string ExpectedCurrency);

    public record Command(Guid AlertId, decimal TargetPrice, string ExpectedCurrency)
    {
        public Guid UserId { get; init; }
    }

    /// <summary>
    /// Same field set as <see cref="GetAlerts.Dto"/>, so the client can drop the result straight
    /// into the list it already has.
    /// </summary>
    public record Response(
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

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.AlertId)
                .NotEmpty();

            RuleFor(x => x.TargetPrice)
                .GreaterThan(0);

            RuleFor(x => x.ExpectedCurrency)
                .NotEmpty()
                .Length(3);
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var alert = await dbContext.Alerts
                .Include(a => a.Product)
                .FirstOrDefaultAsync(
                    a => a.Id == request.AlertId && a.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Alert not found");

            var product = alert.Product;

            // The product's currency is read here, never taken from the request — the client does
            // not get to say what an alert is denominated in. ExpectedCurrency is only used to
            // confirm the user was looking at this same currency when they picked the number.
            if (!string.Equals(request.ExpectedCurrency, product.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(
                    $"This product is now priced in {product.Currency}, not {request.ExpectedCurrency}. " +
                    "Reload and set a target in the current currency.",
                    422,
                    "CurrencyChanged");
            }

            if (!alert.HasCurrencyMismatch(product.Currency))
            {
                throw new ApiException(
                    "This alert is not dormant, so there is nothing to re-denominate.",
                    422,
                    "AlertNotDormant");
            }

            alert.Redenominate(request.TargetPrice, product.Currency);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Alert {AlertId} re-denominated to {Currency}", alert.Id, alert.Currency);

            return new Response(
                alert.Id,
                alert.ProductId,
                product.Name,
                product.CurrentPrice,
                alert.TargetPrice,
                alert.Condition.ToApiString(),
                alert.IsActive,
                alert.LastTriggeredAt,
                alert.Currency,
                product.Currency,
                // Computed rather than hardcoded false, so the one definition of "dormant" stays
                // the only one — a future change to the rule cannot leave this slice stale.
                alert.HasCurrencyMismatch(product.Currency)
            );
        }
    }

    public static void MapRedenominateAlertEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/alerts/{id:guid}/redenominate",
            async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.TargetPrice, request.ExpectedCurrency)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("RedenominateAlert")
        .WithTags("Alerts")
        .WithSummary("Re-denominate a dormant alert")
        .WithDescription(
            "Moves a dormant alert onto its product's current currency with a new target price, " +
            "ending its dormancy. A new target is required: the old one is an amount in the old " +
            "currency and carries no meaning in the new one. Rejects alerts that are not dormant.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
