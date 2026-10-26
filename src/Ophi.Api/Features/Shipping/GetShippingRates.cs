using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Shipping;

public static class GetShippingRates
{
    public static void MapGetShippingRatesEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/shipping-rates", async (IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(new Query(context.User.GetUserId()));
            return Results.Ok(result);
        })
        .WithName("GetShippingRates")
        .WithTags("Shipping")
        .WithSummary("List shipping rates")
        .WithDescription("Returns the current user's flat shipping rates, one per site host, ordered by host.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId);

    public record Response(List<Item> Items);

    public record Item(string Host, decimal Amount, string Currency, decimal? FreeAbove);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching shipping rates for user {UserId}", request.UserId);

            var items = await dbContext.ShippingRates
                .AsNoTracking()
                .Where(r => r.UserId == request.UserId)
                .OrderBy(r => r.Host)
                .Select(r => new Item(r.Host, r.Amount, r.Currency, r.FreeAbove))
                .ToListAsync(cancellationToken);

            return new Response(items);
        }
    }
}
