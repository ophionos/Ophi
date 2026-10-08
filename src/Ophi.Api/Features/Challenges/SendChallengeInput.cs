using FluentValidation;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Scraping;
using Wolverine;

namespace Ophi.Api.Features.Challenges;

public static class SendChallengeInput
{
    public static void MapSendChallengeInputEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/challenge/input", async (Request request, IMessageBus bus, HttpContext context) =>
            {
                await bus.InvokeAsync(new Command(request.Kind, request.X, request.Y, request.Text, request.Key)
                {
                    UserId = context.User.GetUserId()
                });
                return Results.NoContent();
            })
            .WithName("SendChallengeInput")
            .WithTags("Challenges")
            .WithSummary("Send input to the challenge session")
            .WithDescription("Forwards one input to the user's challenge session: 'down' or 'up' presses or releases the mouse at x,y in the 1920x1080 viewport; 'text' types text; 'key' presses one of Enter, Tab, Backspace, Delete, Escape, Space, or the arrow keys.")
            .Produces(204)
            .RequireAuthorization()
            // A press-and-release pair per click, alongside the poll: see GetChallenge.
            .DisableRateLimiting();
    }

    public record Request(string Kind, double? X, double? Y, string? Text, string? Key);

    public record Command(string Kind, double? X, double? Y, string? Text, string? Key)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Kind).Must(k => k is "down" or "up" or "text" or "key")
                .WithMessage("Kind must be down, up, text, or key.");
            When(x => x.Kind is "down" or "up", () =>
            {
                RuleFor(x => x.X).NotNull().InclusiveBetween(0, ChallengeSession.ViewportWidth - 1);
                RuleFor(x => x.Y).NotNull().InclusiveBetween(0, ChallengeSession.ViewportHeight - 1);
            });
            When(x => x.Kind == "text", () =>
                RuleFor(x => x.Text).NotEmpty().MaximumLength(ChallengeSession.MaxTextLength));
            When(x => x.Kind == "key", () =>
                RuleFor(x => x.Key).NotEmpty().Must(k => k != null && ChallengeSession.AllowedKeys.Contains(k))
                    .WithMessage("That key is not allowed."));
        }
    }

    public class Handler(IChallengeSessionManager sessions)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var session = sessions.Find(request.UserId)
                ?? throw new NotFoundException("No challenge session is open.");

            switch (request.Kind)
            {
                case "down":
                case "up":
                    await session.PointerAsync(request.Kind == "down", request.X!.Value, request.Y!.Value);
                    break;
                case "text":
                    await session.TypeAsync(request.Text!);
                    break;
                default:
                    await session.PressAsync(request.Key!);
                    break;
            }
        }
    }
}
