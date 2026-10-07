using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Webhooks;
using Wolverine;

namespace Ophi.Api.Features.Webhooks;

public static class TestWebhookTarget
{
    public static void MapTestWebhookTargetEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/webhooks/{id:guid}/test", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("TestWebhookTarget")
        .WithTags("Webhooks")
        .WithSummary("Send a test payload to a webhook target")
        .WithDescription("Fires a synthetic test payload to verify the webhook URL is reachable.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Command(Guid Id, Guid UserId);

    public record Response(bool Success, string? Error);

    public class Handler(OphiDbContext dbContext, IWebhookDispatchService webhookDispatchService, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var target = await dbContext.WebhookTargets
                .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Webhook target not found");

            logger.LogInformation("Testing webhook target {WebhookId}", request.Id);

            var (success, error) = await webhookDispatchService.SendTestAsync(target.Url, cancellationToken);
            return new Response(success, error);
        }
    }
}
