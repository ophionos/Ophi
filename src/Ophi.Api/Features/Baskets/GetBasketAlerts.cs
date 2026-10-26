using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class GetBasketAlerts
{
    public record Query(Guid BasketId, Guid UserId);

    public class Handler(OphiDbContext dbContext)
    {
        public async Task<BasketAlertListDto> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!await dbContext.Baskets.AnyAsync(b => b.Id == request.BasketId && b.UserId == request.UserId, cancellationToken))
                throw new NotFoundException("Basket not found");

            var alerts = await dbContext.BasketAlerts
                .AsNoTracking()
                .Where(a => a.BasketId == request.BasketId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(cancellationToken);
            var totals = await BasketTotalsReader.ComputeAsync(dbContext, request.BasketId, request.UserId, cancellationToken);

            return new BasketAlertListDto(alerts.Select(a => BasketAlertDto.From(a, totals)).ToList());
        }
    }

    public static void MapGetBasketAlertsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/baskets/{id:guid}/alerts", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<BasketAlertListDto>(new Query(id, context.User.GetUserId()));
            return Results.Ok(result);
        })
        .WithName("GetBasketAlerts")
        .WithTags("Baskets")
        .WithSummary("List a basket's alerts")
        .WithDescription("Returns the basket's alerts, oldest first, each with the store and total it watches now (null when that store has no total) and `hasCurrencyMismatch` (the watched total is in another currency than the target, so the alert is dormant). Returns 404 for a basket that belongs to another user.")
        .Produces<BasketAlertListDto>(200)
        .RequireAuthorization();
}
