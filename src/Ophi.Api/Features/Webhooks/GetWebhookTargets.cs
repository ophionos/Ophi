using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Webhooks;

public static class GetWebhookTargets
{
    public static void MapGetWebhookTargetsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/webhooks", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<List<Response>>(query);
            return Results.Ok(result);
        })
        .WithName("GetWebhookTargets")
        .WithTags("Webhooks")
        .WithSummary("List webhook targets")
        .WithDescription("Returns all outbound webhook targets for the current user.")
        .Produces<List<Response>>(200)
        .RequireAuthorization();
    }

    public record Query
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Name,
        string Url,
        List<string> Events,
        bool IsEnabled,
        DateTime CreatedAt);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<List<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching webhook targets for user {UserId}", request.UserId);

            var targets = await dbContext.WebhookTargets
                .Where(w => w.UserId == request.UserId)
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new Response(w.Id, w.Name, w.Url, w.Events, w.IsEnabled, w.CreatedAt))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count} webhook targets for user {UserId}", targets.Count, request.UserId);
            return targets;
        }
    }
}
