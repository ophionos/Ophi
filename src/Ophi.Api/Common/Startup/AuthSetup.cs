using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Middleware;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.Startup;

internal static class AuthSetup
{
    /// <summary>
    /// Wires up the dual authentication scheme: cookie sessions for the browser, API keys
    /// (Bearer header) for scripts. The "Smart" policy scheme inspects the Authorization
    /// header to pick which scheme handles each request.
    /// </summary>
    public static IServiceCollection AddOphiAuth(
        this IServiceCollection services, IWebHostEnvironment env, IConfiguration configuration)
    {
        // Persist Data Protection keys so session cookies survive container/process
        // restarts. Without this, keys live in the container's ephemeral filesystem and
        // every redeploy regenerates them, logging every user out. Keys go in the directory
        // DATABASE_PATH names (the persistent data volume). Skipped under Testing so the
        // integration test factory keeps its default in-memory keyring.
        if (!env.IsEnvironment("Testing"))
        {
            var databasePath = configuration["DATABASE_PATH"] ?? "./data/ophi.db";
            var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
                ?? Directory.GetCurrentDirectory();
            var keysDirectory = Path.Combine(dataDirectory, "dp-keys");
            Directory.CreateDirectory(keysDirectory);

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
                .SetApplicationName("Ophi");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = "Smart";
                options.DefaultChallengeScheme = "Smart";
            })
            .AddPolicyScheme("Smart", "Cookie or API Key", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    return authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                        ? ApiKeyAuthenticationHandler.SchemeName
                        : CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "Ophi.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = env.IsProduction()
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = async context =>
                {
                    var requestServices = context.HttpContext.RequestServices;
                    var validator = requestServices.GetRequiredService<SecurityStampGuard>();
                    var dbContext = requestServices.GetRequiredService<OphiDbContext>();

                    var valid = await validator.ValidateAsync(
                        context.Principal, dbContext, context.HttpContext.RequestAborted);
                    if (!valid)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddSingleton<SecurityStampGuard>();
        services.AddAuthorization(options =>
            options.AddPolicy(AuthPolicies.SessionOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    !context.User.HasClaim(c => c.Type == ApiKeyScopeMiddleware.ScopesClaim))));
        return services;
    }
}
