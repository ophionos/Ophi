using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Startup;

namespace Ophi.Api.Tests.Unit.Middleware;

/// <summary>
/// The API sits behind a proxy in every deployment (SvelteKit hook on compose, Caddy on the Pi),
/// so RemoteIpAddress is the proxy unless X-Forwarded-For is honoured. These tests run the same
/// forwarded-headers + rate-limit registration Program.cs uses, in the Testing environment (real
/// caps — Development is effectively unlimited), because Program.cs skips UseRateLimiter under
/// the integration-test factory.
/// </summary>
public class ForwardedHeadersTests
{
    private const string ComposeBridge = "172.16.0.0/12";
    private static readonly IPAddress TrustedProxy = IPAddress.Parse("172.18.0.5");
    private static readonly IPAddress UntrustedClient = IPAddress.Parse("203.0.113.9");

    private static async Task<WebApplication> StartAppAsync(string? knownIpNetworks)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        if (knownIpNetworks is not null)
            builder.Configuration["ForwardedHeaders:KnownIPNetworks"] = knownIpNetworks;

        builder.Services.AddOphiForwardedHeaders(builder.Configuration);
        builder.Services.AddOphiRateLimiting(builder.Environment);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("auth");
        app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "");

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<HttpContext> SendAsync(
        WebApplication app, string method, string path, IPAddress remoteIp, string? forwardedFor) =>
        app.GetTestServer().SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = remoteIp;
            if (forwardedFor is not null)
                context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }, TestContext.Current.CancellationToken);

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        return await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuthPolicy_TrustedProxyWithDistinctForwardedClients_PartitionsSeparately()
    {
        await using var app = await StartAppAsync(ComposeBridge);

        for (var i = 0; i < 10; i++)
        {
            var ok = await SendAsync(app, "POST", "/login", TrustedProxy, "198.51.100.1");
            ok.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        var exhausted = await SendAsync(app, "POST", "/login", TrustedProxy, "198.51.100.1");
        var otherClient = await SendAsync(app, "POST", "/login", TrustedProxy, "198.51.100.2");

        exhausted.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        otherClient.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task AuthPolicy_UntrustedSourceWithForwardedHeader_IgnoresHeader()
    {
        await using var app = await StartAppAsync(ComposeBridge);

        // A client talking to the API directly rotates X-Forwarded-For on every request. If the
        // header were honoured each request would land in a fresh partition and never hit the cap.
        for (var i = 0; i < 10; i++)
        {
            var ok = await SendAsync(app, "POST", "/login", UntrustedClient, $"198.51.100.{i + 1}");
            ok.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        var rotated = await SendAsync(app, "POST", "/login", UntrustedClient, "198.51.100.200");

        rotated.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task ForwardedFor_FromLoopbackWithNoConfiguredNetworks_IsTrusted()
    {
        await using var app = await StartAppAsync(knownIpNetworks: null);

        var context = await SendAsync(app, "GET", "/ip", IPAddress.Loopback, "198.51.100.1");

        (await ReadBodyAsync(context)).Should().Be("198.51.100.1");
    }

    [Fact]
    public async Task ForwardedFor_FromBridgeWithNoConfiguredNetworks_IsIgnored()
    {
        await using var app = await StartAppAsync(knownIpNetworks: null);

        var context = await SendAsync(app, "GET", "/ip", TrustedProxy, "198.51.100.1");

        (await ReadBodyAsync(context)).Should().Be("172.18.0.5");
    }

    [Fact]
    public async Task ForwardedFor_FromIpv4MappedTrustedProxy_IsTrusted()
    {
        // Kestrel listening on a dual-mode socket reports IPv4 peers as ::ffff:a.b.c.d.
        await using var app = await StartAppAsync(ComposeBridge);

        var context = await SendAsync(app, "GET", "/ip", TrustedProxy.MapToIPv6(), "198.51.100.1");

        (await ReadBodyAsync(context)).Should().Be("198.51.100.1");
    }

    [Fact]
    public async Task ForwardedFor_WithMultipleEntries_UsesOnlyTheLastHop()
    {
        await using var app = await StartAppAsync(ComposeBridge);

        var context = await SendAsync(app, "GET", "/ip", TrustedProxy, "10.9.9.9, 198.51.100.1");

        (await ReadBodyAsync(context)).Should().Be("198.51.100.1");
    }

    [Fact]
    public void AddOphiForwardedHeaders_WithMultipleNetworks_AddsEachToKnownNetworks()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownIPNetworks"] = " 172.16.0.0/12 , 10.0.0.0/8 "
            })
            .Build();

        services.AddOphiForwardedHeaders(configuration);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        options.ForwardLimit.Should().Be(1);
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor);
    }

    [Fact]
    public void AddOphiForwardedHeaders_WithInvalidNetwork_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownIPNetworks"] = "not-a-cidr"
            })
            .Build();

        var act = () => services.AddOphiForwardedHeaders(configuration);

        act.Should().Throw<FormatException>();
    }
}
