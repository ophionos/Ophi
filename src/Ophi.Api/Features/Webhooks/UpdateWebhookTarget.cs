using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Webhooks;

public static class UpdateWebhookTarget
{
    public static void MapUpdateWebhookTargetEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/webhooks/{id:guid}", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.Name, request.Url, request.Events, request.IsEnabled)
            {
                UserId = context.User.GetUserId()
            };
            var result = await bus.InvokeAsync<GetWebhookTargets.Response>(command);
            return Results.Ok(result);
        })
        .WithName("UpdateWebhookTarget")
        .WithTags("Webhooks")
        .WithSummary("Update a webhook target")
        .Produces<GetWebhookTargets.Response>(200)
        .RequireAuthorization();
    }

    public record Request(string Name, string Url, List<string> Events, bool IsEnabled);

    public record Command(Guid Id, string Name, string Url, List<string> Events, bool IsEnabled)
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
            var target = await dbContext.WebhookTargets
                .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Webhook target not found");

            target.Name = request.Name;
            target.Url = request.Url;
            target.Events = request.Events.Distinct().ToList();
            target.IsEnabled = request.IsEnabled;

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Webhook target {WebhookId} updated", target.Id);

            return new GetWebhookTargets.Response(target.Id, target.Name, target.Url, target.Events, target.IsEnabled, target.CreatedAt);
        }
    }
}
