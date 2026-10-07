using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Webhooks;

public static class DeleteWebhookTarget
{
    public static void MapDeleteWebhookTargetEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/webhooks/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteWebhookTarget")
        .WithTags("Webhooks")
        .WithSummary("Delete a webhook target")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid Id, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var target = await dbContext.WebhookTargets
                .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Webhook target not found");

            dbContext.WebhookTargets.Remove(target);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Webhook target {WebhookId} deleted", request.Id);
        }
    }
}
