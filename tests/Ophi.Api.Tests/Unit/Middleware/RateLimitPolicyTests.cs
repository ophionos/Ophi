using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ophi.Api.Common;

namespace Ophi.Api.Tests.Unit.Middleware;

public class RateLimitPolicyTests
{
    [Fact]
    public void PolicyNames_AreDistinct()
    {
        var names = new[]
        {
            RateLimitPolicies.ProductCreation,
            RateLimitPolicies.AlertCreation,
            RateLimitPolicies.StoreCreation,
            RateLimitPolicies.WebhookCreation
        };

        names.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(RateLimitPolicies.ProductCreation, 50)]
    [InlineData(RateLimitPolicies.AlertCreation, 100)]
    [InlineData(RateLimitPolicies.StoreCreation, 20)]
    [InlineData(RateLimitPolicies.WebhookCreation, 10)]
    public void GetLimiter_ReturnsCorrectPermitLimit(string policyName, int expectedLimit)
    {
        var options = RateLimitPolicies.GetLimiterOptions(policyName);

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(expectedLimit);
    }

    [Theory]
    [InlineData(RateLimitPolicies.ProductCreation)]
    [InlineData(RateLimitPolicies.AlertCreation)]
    [InlineData(RateLimitPolicies.StoreCreation)]
    [InlineData(RateLimitPolicies.WebhookCreation)]
    public void GetLimiter_UsesOneHourWindow(string policyName)
    {
        var options = RateLimitPolicies.GetLimiterOptions(policyName);

        options!.Window.Should().Be(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData(RateLimitPolicies.ProductCreation)]
    [InlineData(RateLimitPolicies.AlertCreation)]
    [InlineData(RateLimitPolicies.StoreCreation)]
    [InlineData(RateLimitPolicies.WebhookCreation)]
    public void GetLimiter_UsesSlidingWindowSegments(string policyName)
    {
        var options = RateLimitPolicies.GetLimiterOptions(policyName);

        options!.SegmentsPerWindow.Should().BeGreaterThan(1);
    }

    [Fact]
    public void GetPartitionKey_UsesUserIdWhenAuthenticated()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));

        var key = RateLimitPolicies.GetPartitionKey(context);

        key.Should().Be("user-123");
    }

    [Fact]
    public void GetPartitionKey_FallsBackToIpWhenAnonymous()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");

        var key = RateLimitPolicies.GetPartitionKey(context);

        key.Should().Be("192.168.1.1");
    }

    [Fact]
    public void GetPartitionKey_ReturnsAnonymousWhenNoIdentity()
    {
        var context = new DefaultHttpContext();

        var key = RateLimitPolicies.GetPartitionKey(context);

        key.Should().Be("anonymous");
    }

    [Fact]
    public void GetLimiterOptions_ReturnsNullForUnknownPolicy()
    {
        var options = RateLimitPolicies.GetLimiterOptions("nonexistent");

        options.Should().BeNull();
    }

    [Fact]
    public async Task OnRejected_Returns429WithJsonBody()
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
        context.RequestServices = new FakeServiceProvider();

        await RateLimitPolicies.HandleRejection(context, TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(429);
        context.Response.ContentType.Should().Be("application/json");
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("TooManyRequests");
    }

    [Fact]
    public async Task OnRejected_LogsWarningWithUserContext()
    {
        var logger = new FakeLogger();
        var factory = new FakeLoggerFactory(logger);
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-456")], "test"));
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        context.Request.Path = "/api/v1/products";
        context.RequestServices = new FakeServiceProvider(factory);

        await RateLimitPolicies.HandleRejection(context, TestContext.Current.CancellationToken);

        logger.LastLogLevel.Should().Be(LogLevel.Warning);
        logger.LastMessage.Should().Contain("user-456");
        logger.LastMessage.Should().Contain("/api/v1/products");
    }

    private class FakeServiceProvider(FakeLoggerFactory? factory = null) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory))
                return factory ?? new FakeLoggerFactory();
            return null;
        }
    }

    private class FakeLoggerFactory(FakeLogger? logger = null) : ILoggerFactory
    {
        public FakeLogger Logger { get; } = logger ?? new FakeLogger();
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => Logger;
        public void Dispose() { }
    }

    private class FakeLogger : ILogger
    {
        public LogLevel LastLogLevel { get; private set; }
        public string LastMessage { get; private set; } = "";

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastLogLevel = logLevel;
            LastMessage = formatter(state, exception);
        }
    }
}
