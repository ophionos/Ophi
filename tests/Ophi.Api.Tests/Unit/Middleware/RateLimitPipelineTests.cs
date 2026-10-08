using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ophi.Api.Common;
using Ophi.Api.Common.Startup;

namespace Ophi.Api.Tests.Unit.Middleware;

/// <summary>
/// Runs the real authentication + rate-limit ordering (<see cref="RateLimitSetup.UseOphiAuthenticationAndRateLimiting"/>,
/// the same call Program.cs makes) with a header-driven test scheme, so per-user partitions are observable.
/// All callers share one IP, like every user behind the compose stack's single proxy address.
/// </summary>
public class RateLimitPipelineTests
{
    private static readonly IPAddress SharedIp = IPAddress.Parse("203.0.113.7");

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddOphiForwardedHeaders(builder.Configuration);
        builder.Services.AddOphiRateLimiting(builder.Environment);
        builder.Services.AddAuthentication(HeaderAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseOphiAuthenticationAndRateLimiting(enableRateLimiting: true);
        app.MapGet("/read", () => Results.Ok());
        app.MapPost("/webhooks", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.WebhookCreation);
        app.MapPost("/detect", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.OutboundFetch);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<int> SendAsync(WebApplication app, string method, string path, string? userId)
    {
        var context = await app.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = method;
            c.Request.Path = path;
            c.Connection.RemoteIpAddress = SharedIp;
            if (userId is not null)
                c.Request.Headers[HeaderAuthHandler.HeaderName] = userId;
        }, TestContext.Current.CancellationToken);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task CreationPolicy_TwoUsersFromOneIp_GetSeparateBuckets()
    {
        await using var app = await StartAppAsync();
        var limit = RateLimitPolicies.GetLimiterOptions(RateLimitPolicies.WebhookCreation)!.PermitLimit;

        for (var i = 0; i < limit; i++)
            (await SendAsync(app, "POST", "/webhooks", "user-a")).Should().Be(StatusCodes.Status200OK);

        (await SendAsync(app, "POST", "/webhooks", "user-a")).Should().Be(StatusCodes.Status429TooManyRequests);
        (await SendAsync(app, "POST", "/webhooks", "user-b")).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GlobalLimiter_TwoUsersFromOneIp_GetSeparateBuckets()
    {
        await using var app = await StartAppAsync();

        for (var i = 0; i < RateLimitSetup.GlobalPerPartitionLimit; i++)
            (await SendAsync(app, "GET", "/read", "user-a")).Should().Be(StatusCodes.Status200OK);

        (await SendAsync(app, "GET", "/read", "user-a")).Should().Be(StatusCodes.Status429TooManyRequests);
        (await SendAsync(app, "GET", "/read", "user-b")).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GlobalLimiter_AnonymousCallers_ShareTheIpBucket()
    {
        await using var app = await StartAppAsync();

        for (var i = 0; i < RateLimitSetup.GlobalPerPartitionLimit; i++)
            (await SendAsync(app, "GET", "/read", userId: null)).Should().Be(StatusCodes.Status200OK);

        (await SendAsync(app, "GET", "/read", userId: null)).Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task OutboundFetchPolicy_IsPerUser()
    {
        await using var app = await StartAppAsync();
        var limit = RateLimitPolicies.GetLimiterOptions(RateLimitPolicies.OutboundFetch)!.PermitLimit;

        for (var i = 0; i < limit; i++)
            (await SendAsync(app, "POST", "/detect", "user-a")).Should().Be(StatusCodes.Status200OK);

        (await SendAsync(app, "POST", "/detect", "user-a")).Should().Be(StatusCodes.Status429TooManyRequests);
        (await SendAsync(app, "POST", "/detect", "user-b")).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task PreAuthGuard_CapsOneIp_BeforeAuthenticationRuns()
    {
        // Each request claims a fresh user, so the per-user limiters never trip. Only the per-IP guard
        // in front of authentication (where an API-key lookup hits the database) stops the flood.
        await using var app = await StartAppAsync();

        for (var i = 0; i < RateLimitSetup.PreAuthPerIpLimit; i++)
            (await SendAsync(app, "GET", "/read", $"user-{i}")).Should().Be(StatusCodes.Status200OK);

        (await SendAsync(app, "GET", "/read", "user-fresh")).Should().Be(StatusCodes.Status429TooManyRequests);
        HeaderAuthHandler.AuthenticationsFor("user-fresh").Should().Be(0);
    }

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";
        public const string HeaderName = "X-Test-User";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Seen = new();

        public static int AuthenticationsFor(string userId) => Seen.GetValueOrDefault(userId);

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers[HeaderName].ToString();
            if (string.IsNullOrEmpty(userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            Seen.AddOrUpdate(userId, 1, (_, n) => n + 1);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
