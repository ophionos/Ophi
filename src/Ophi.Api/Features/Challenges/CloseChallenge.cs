using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Api.Features.Challenges;

public static class CloseChallenge
{
    public static void MapCloseChallengeEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/challenge", async (IChallengeSessionManager sessions, HttpContext context) =>
            {
                await sessions.CloseAsync(context.User.GetUserId());
                return Results.NoContent();
            })
            .WithName("CloseChallenge")
            .WithTags("Challenges")
            .WithSummary("Close the challenge session")
            .WithDescription("Closes the user's challenge session without saving anything. Succeeds when there is no session.")
            .Produces(204)
            .RequireAuthorization();
    }
}
