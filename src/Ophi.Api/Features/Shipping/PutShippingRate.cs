using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Wolverine;

namespace Ophi.Api.Features.Shipping;

public static class PutShippingRate
{
    public static void MapPutShippingRateEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/shipping-rates/{host}", async (string host, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(host, request.Amount, request.Currency, request.FreeAbove)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("PutShippingRate")
        .WithTags("Shipping")
        .WithSummary("Create or replace a shipping rate")
        .WithDescription("Sets the flat shipping cost for one site. The host in the route is trimmed, lowercased and stripped of a leading 'www.' (WWW.Amazon.ES becomes amazon.es). " +
                         "Creates the rate or replaces the existing one for that host, and returns 200 in both cases. " +
                         "Shipping is never converted between currencies: it applies only to prices in the rate's currency. " +
                         "freeAbove is optional; when set, an item priced at or above it ships free.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Request(decimal Amount, string Currency, decimal? FreeAbove = null);

    public record Command(string Host, decimal Amount, string Currency, decimal? FreeAbove)
    {
        public Guid UserId { get; init; }
    }

    public record Response(string Host, decimal Amount, string Currency, decimal? FreeAbove);

    /// <summary>The stored form of a host: trimmed, lowercase, no "www.". The one place it is derived.</summary>
    public static string NormalizeHost(string host) => SiteHost.Normalize(host.Trim());

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Host)
                .NotEmpty()
                .Must(h => h is not null && NormalizeHost(h) is { Length: > 0 and <= ShippingRateConfiguration.HostMaxLength } n
                           && !n.Any(c => c is '/' or ':' || char.IsWhiteSpace(c)))
                .WithMessage("Host must be a bare site host such as amazon.es (no scheme, path, port or spaces)");

            RuleFor(x => x.Amount)
                .GreaterThanOrEqualTo(0m)
                .PrecisionScale(18, 2, true);

            RuleFor(x => x.FreeAbove)
                .GreaterThan(0m)
                .PrecisionScale(18, 2, true)
                .When(x => x.FreeAbove.HasValue);

            RuleFor(x => x.Currency)
                .NotEmpty()
                .Matches("^[A-Za-z]{3}$")
                .WithMessage("Currency must be a 3-letter code (e.g. EUR)");
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var host = NormalizeHost(request.Host);
            var currency = request.Currency.ToUpperInvariant();

            var rate = await dbContext.ShippingRates
                .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.Host == host, cancellationToken);

            if (rate == null)
            {
                rate = new ShippingRate
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    Host = host,
                    Amount = request.Amount,
                    Currency = currency,
                    FreeAbove = request.FreeAbove
                };
                dbContext.ShippingRates.Add(rate);
            }
            else
            {
                rate.Update(request.Amount, currency, request.FreeAbove);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Shipping rate for {Host} set by user {UserId}", host, request.UserId);
            return new Response(rate.Host, rate.Amount, rate.Currency, rate.FreeAbove);
        }
    }
}
