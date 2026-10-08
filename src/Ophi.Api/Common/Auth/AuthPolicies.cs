namespace Ophi.Api.Common.Auth;

internal static class AuthPolicies
{
    /// <summary>
    /// A signed-in browser session; API-key principals are refused with 403. For endpoints that
    /// manage credentials: a leaked key that could mint a new non-expiring key would outlive its
    /// own expiry and revocation.
    /// </summary>
    public const string SessionOnly = "SessionOnly";
}
