using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.ApiKeys;

public static class DeleteApiKey
{
    public static void MapDeleteApiKeyEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/api-keys/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteApiKey")
        .WithTags("ApiKeys")
        .WithSummary("Revoke an API key")
        .WithDescription("Permanently deletes an API key. Any scripts using this key will immediately lose access.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid Id, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var apiKey = await dbContext.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == request.Id && k.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("API key not found");

            dbContext.ApiKeys.Remove(apiKey);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("API key {ApiKeyId} revoked for user {UserId}", request.Id, request.UserId);
        }
    }
}
