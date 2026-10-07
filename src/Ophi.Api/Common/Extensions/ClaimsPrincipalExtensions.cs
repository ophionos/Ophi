using System.Security.Claims;
using Ophi.Api.Common.Exceptions;

namespace Ophi.Api.Common.Extensions;

public static class ClaimsPrincipalExtensions
{
    extension(ClaimsPrincipal principal)
    {
        public Guid GetUserId() => !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            ? throw new UnauthorizedException("User ID claim is missing or invalid")
            : userId;
    }
}
