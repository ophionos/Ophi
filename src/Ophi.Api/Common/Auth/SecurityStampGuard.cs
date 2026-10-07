using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.Auth;

/// <summary>
/// Validates the security-stamp claim on cookie tickets against the user's current stamp,
/// so rotating the stamp (password change) invalidates every session issued before it.
/// Stamps are cached briefly to keep steady-state requests off the database; the TTL is
/// the worst-case window in which a revoked session still works. Tickets without a stamp
/// claim (issued before this feature) are rejected, forcing a one-time re-login.
/// </summary>
public class SecurityStampGuard(IMemoryCache cache)
{
    public const string ClaimType = "ophi:security_stamp";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<bool> ValidateAsync(ClaimsPrincipal? principal, OphiDbContext dbContext, CancellationToken cancellationToken)
    {
        var stampClaim = principal?.FindFirstValue(ClaimType);
        var userIdValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(stampClaim) || !Guid.TryParse(userIdValue, out var userId))
        {
            return false;
        }

        if (!cache.TryGetValue(CacheKey(userId), out string? currentStamp))
        {
            currentStamp = await dbContext.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(cancellationToken);

            // Unknown users are not cached negatively — a user created moments later
            // (or a race with registration) must not be locked out for a TTL.
            if (currentStamp is not null)
            {
                cache.Set(CacheKey(userId), currentStamp, CacheTtl);
            }
        }

        return currentStamp == stampClaim;
    }

    /// <summary>
    /// Primes the cache with a freshly rotated stamp so old sessions die immediately
    /// instead of after the cache TTL, and the rotating session's new cookie is valid at once.
    /// </summary>
    public void Refresh(Guid userId, string securityStamp) =>
        cache.Set(CacheKey(userId), securityStamp, CacheTtl);

    /// <summary>
    /// Drops the cached stamp so the next request hits the database. Used on account
    /// deletion, where there is no new stamp to refresh with and the cached one would
    /// keep the user's sessions alive until the TTL expired.
    /// </summary>
    public void Evict(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"security-stamp:{userId}";
}
