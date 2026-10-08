using Ophi.Infrastructure.Scraping;

namespace Ophi.Api.Features.Challenges;

public static class GetChallengeAvailability
{
    public static void MapGetChallengeAvailabilityEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/challenge/available",
                (IChallengeSessionManager sessions) => Results.Ok(new Response(sessions.IsEnabled)))
            .WithName("GetChallengeAvailability")
            .WithTags("Challenges")
            .WithSummary("Check whether challenge solving is available")
            .WithDescription("Returns whether this server can open a remote browser session in which the user solves a store's anti-bot challenge. The operator turns it on with CHALLENGE_SOLVING.")
            .Produces<Response>()
            .RequireAuthorization();
    }

    public record Response(bool Available);
}
