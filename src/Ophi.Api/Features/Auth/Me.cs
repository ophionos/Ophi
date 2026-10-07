using System.Security.Claims;

namespace Ophi.Api.Features.Auth;

public static class Me
{
    public static void MapMeEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/auth/me", (HttpContext context) =>
        {
            var user = context.User;

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = user.FindFirstValue(ClaimTypes.Email);
            var name = user.FindFirstValue(ClaimTypes.Name);

            return Results.Ok(new { id, email, name });
        })
        .WithName("Me")
        .WithTags("Auth")
        .WithSummary("Get current user profile")
        .WithDescription("Returns the ID, email, and name of the currently authenticated user. Used by the frontend to verify session state.")
        .Produces(200)
        .RequireAuthorization();
}
