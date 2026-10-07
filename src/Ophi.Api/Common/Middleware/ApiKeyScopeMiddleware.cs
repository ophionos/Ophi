namespace Ophi.Api.Common.Middleware;

/// <summary>
/// Enforces the <c>write</c> scope on mutating requests made with an API key.
///
/// Scopes were previously validated on creation, stored, and shown back in the UI, but never
/// checked anywhere — a key the user created as read-only had full write access, including DELETE
/// on their products. That made the scope a misleading security control: the danger is trusting the
/// label and putting the key somewhere a full-access key would never go.
///
/// This is middleware rather than a per-endpoint policy for the same reason
/// <see cref="CsrfMiddleware"/> is: a blanket rule over non-safe methods cannot be forgotten when a
/// new vertical slice is added, whereas ~70 individual <c>RequireAuthorization("write")</c> calls
/// can. Must be registered AFTER <c>UseAuthentication()</c> — it reads the authenticated principal.
///
/// Cookie sessions carry no scopes claim and are deliberately unaffected. Reads are allowed for any
/// valid key: a key must already have at least one scope, and "write" is treated as implying read.
/// </summary>
public class ApiKeyScopeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>Claim emitted by <c>ApiKeyAuthenticationHandler</c>; only API-key principals have it.</summary>
    internal const string ScopesClaim = "api_key_scopes";

    internal const string WriteScope = "write";

    public async Task InvokeAsync(HttpContext context)
    {
        var scopes = context.User.FindFirst(ScopesClaim)?.Value;

        if (scopes is not null &&
            !SafeMethods.Contains(context.Request.Method) &&
            !HasWriteScope(scopes))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"InsufficientScope","message":"This API key does not have the 'write' scope."}""");
            return;
        }

        await next(context);
    }

    private static bool HasWriteScope(string scopes) =>
        scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(WriteScope, StringComparer.OrdinalIgnoreCase);
}
