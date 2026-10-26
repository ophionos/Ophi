using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Shipping;

public static class DeleteShippingRate
{
    public static void MapDeleteShippingRateEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/shipping-rates/{host}", async (string host, IMessageBus bus, HttpContext context) =>
        {
            await bus.InvokeAsync(new Command(host, context.User.GetUserId()));
            return Results.NoContent();
        })
        .WithName("DeleteShippingRate")
        .WithTags("Shipping")
        .WithSummary("Delete a shipping rate")
        .WithDescription("Deletes the current user's shipping rate for a host (normalized like PUT). Returns 404 when there is none.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(string Host, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var host = PutShippingRate.NormalizeHost(request.Host);

            var rate = await dbContext.ShippingRates
                .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.Host == host, cancellationToken)
                ?? throw new NotFoundException("Shipping rate not found");

            dbContext.ShippingRates.Remove(rate);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Shipping rate for {Host} deleted by user {UserId}", host, request.UserId);
        }
    }
}
