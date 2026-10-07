using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Ophi.Api.Features.Auth;

public static class Logout
{
    public static void MapLogoutEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .WithName("Logout")
        .WithTags("Auth")
        .WithSummary("Log out the current user")
        .WithDescription("Signs out the current user by clearing the authentication cookie.")
        .Produces(204)
        .RequireAuthorization();
}
