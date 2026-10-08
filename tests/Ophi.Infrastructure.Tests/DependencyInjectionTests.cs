using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Webhooks;

namespace Ophi.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddInfrastructure_WithNoSmtpConfig_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddInfrastructure_WithAllSmtpFields_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_USER"] = "user@example.com",
            ["SMTP_PASS"] = "secret",
            ["SMTP_FROM"] = "noreply@example.com"
        });

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("SMTP_USER")]
    [InlineData("SMTP_PASS")]
    [InlineData("SMTP_FROM")]
    public void AddInfrastructure_WithSmtpHostButMissingField_Throws(string missingKey)
    {
        var services = new ServiceCollection();
        var values = new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_USER"] = "user@example.com",
            ["SMTP_PASS"] = "secret",
            ["SMTP_FROM"] = "noreply@example.com",
            [missingKey] = null
        };
        var config = BuildConfig(values);

        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{missingKey}*");
    }

    [Fact]
    public void AddInfrastructure_WithSmtpHostOnly_ThrowsListingAllMissingFields()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com"
        });

        var act = () => services.AddInfrastructure(config);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("SMTP_USER")
            .And.Contain("SMTP_PASS")
            .And.Contain("SMTP_FROM");
    }

    // The webhook URL is the credential. The default HttpClient logging handlers write the full
    // request URI, so these clients must be resolved through the typed-client registration that
    // carries RemoveAllLoggers — resolving the interface must not fall back to the default client.
    [Fact]
    public async Task IDiscordService_WhenSending_DoesNotLogWebhookUrl()
    {
        var (provider, logs) = BuildProviderCapturingLogs();
        using var scope = provider.CreateScope();
        var discord = scope.ServiceProvider.GetRequiredService<IDiscordService>();

        await discord.SendPriceAlertAsync(
            new DiscordPriceAlert("Item", "https://example.com", 9.99m, 10m, "USD", AlertCondition.Below),
            "https://discord.com/api/webhooks/123/SECRET-TOKEN",
            TestContext.Current.CancellationToken);

        logs.Should().NotContain(m => m.Contains("SECRET-TOKEN"));
    }

    [Fact]
    public async Task IWebhookDispatchService_WhenSendingTest_DoesNotLogTargetUrl()
    {
        var (provider, logs) = BuildProviderCapturingLogs();
        using var scope = provider.CreateScope();
        var webhooks = scope.ServiceProvider.GetRequiredService<IWebhookDispatchService>();

        var (success, _) = await webhooks.SendTestAsync(
            "https://hooks.example.com/services/SECRET-TOKEN", TestContext.Current.CancellationToken);

        success.Should().BeTrue();
        logs.Should().NotContain(m => m.Contains("SECRET-TOKEN"));
    }

    private static (ServiceProvider Provider, ConcurrentQueue<string> Logs) BuildProviderCapturingLogs()
    {
        var logs = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(logs)));
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["POSTGRES_CONNECTION"] = "Host=unused"
        }));
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(() => new StubHandler()));
        return (services.BuildServiceProvider(), logs);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => logs.Enqueue(formatter(state, exception));
        }
    }
}
