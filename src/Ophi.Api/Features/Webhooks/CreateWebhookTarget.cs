using FluentValidation;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Webhooks;

public static class CreateWebhookTarget
{
    public static void MapCreateWebhookTargetEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/webhooks", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Url, request.Events, request.IsEnabled)
            {
                UserId = context.User.GetUserId()
            };
            var result = await bus.InvokeAsync<GetWebhookTargets.Response>(command);
            return Results.Created($"/api/v1/webhooks/{result.Id}", result);
        })
        .WithName("CreateWebhookTarget")
        .WithTags("Webhooks")
        .WithSummary("Create a webhook target")
        .WithDescription("Creates a new outbound webhook target. Fires a JSON payload to the URL when the specified events occur.")
        .Produces<GetWebhookTargets.Response>(201)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.WebhookCreation);
    }

    public record Request(string Name, string Url, List<string> Events, bool IsEnabled = true);

    public record Command(string Name, string Url, List<string> Events, bool IsEnabled)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(WebhookAddressPolicy addressPolicy)
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Url).NotEmpty().MaximumLength(2048).MustBeAllowedWebhookUrl(addressPolicy);
            RuleFor(x => x.Events).NotEmpty().WithMessage("At least one event type is required.").MustContainValidEvents();
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<GetWebhookTargets.Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var target = new WebhookTarget
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                Url = request.Url,
                Events = request.Events.Distinct().ToList(),
                IsEnabled = request.IsEnabled
            };

            dbContext.WebhookTargets.Add(target);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Webhook target {WebhookId} created for user {UserId}", target.Id, request.UserId);

            return new GetWebhookTargets.Response(target.Id, target.Name, target.Url, target.Events, target.IsEnabled, target.CreatedAt);
        }
    }
}
