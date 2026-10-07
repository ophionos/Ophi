using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Ophi.Api.Common.Auth;

namespace Ophi.Api.Features.Auth;

internal static class AuthExtensions
{
    /// <summary>
    /// Signs the user in by creating a cookie-based authentication session. The security
    /// stamp travels in the ticket and is checked against the database on every request —
    /// rotating it (password change) invalidates the session.
    /// </summary>
    public static Task SignInUserAsync(this HttpContext context, Guid id, string email, string name, string securityStamp)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name),
            new(SecurityStampGuard.ClaimType, securityStamp)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }
}
