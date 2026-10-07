using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class DeleteStore
{
    public static void MapDeleteStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/stores/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteStore")
        .WithTags("Stores")
        .WithSummary("Delete a store configuration")
        .WithDescription("Permanently deletes a store configuration. Products using this store will fall back to auto-detection for future scrapes.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid Id, Guid UserId);

    public class Handler(OphiDbContext dbContext, IStoreConfigProvider configProvider, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var store = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.Id == request.Id && s.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Store not found");
            
            dbContext.StoreConfigurations.Remove(store);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Invalidate cache for this user
            configProvider.InvalidateCache(request.UserId);

            logger.LogInformation("Store {StoreId} deleted by user {UserId}", request.Id, request.UserId);
        }
    }
}
