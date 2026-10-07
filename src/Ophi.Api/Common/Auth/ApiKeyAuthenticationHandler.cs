using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.Auth;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    internal static readonly TimeSpan LastUsedThrottleInterval = TimeSpan.FromMinutes(1);

    // Per-process cache of the most recent LastUsedAt we wrote, keyed by ApiKey.Id.
    // Reduces a per-request DB write to once-per-minute per key. Multi-instance deployments
    // each maintain their own cache; the small redundancy is acceptable for the gain.
    private static readonly ConcurrentDictionary<Guid, DateTime> LastUsedWrites = new();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawKey = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.Fail("Empty API key");

        var keyHash = HashKey(rawKey);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();

        var apiKey = await dbContext.ApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, Context.RequestAborted);

        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid API key");

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < timeProvider.GetUtcNow().UtcDateTime)
            return AuthenticateResult.Fail("API key has expired");

        await TouchLastUsedAsync(dbContext, apiKey.Id);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.UserId.ToString()),
            new(ClaimTypes.Email, apiKey.User.Email),
            new(ClaimTypes.Name, apiKey.User.Name),
            new("api_key_id", apiKey.Id.ToString()),
            new("api_key_scopes", string.Join(',', apiKey.Scopes))
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    private async Task TouchLastUsedAsync(OphiDbContext dbContext, Guid apiKeyId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (LastUsedWrites.TryGetValue(apiKeyId, out var lastWrite) &&
            now - lastWrite < LastUsedThrottleInterval)
        {
            return;
        }

        await dbContext.ApiKeys
            .Where(k => k.Id == apiKeyId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(k => k.LastUsedAt, now),
                Context.RequestAborted);

        LastUsedWrites[apiKeyId] = now;
    }

    internal static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(bytes);
    }

    // Test helper: clear the throttle cache so unit tests can simulate fresh state.
    internal static void ResetLastUsedCacheForTests() => LastUsedWrites.Clear();
}
