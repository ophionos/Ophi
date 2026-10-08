using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Net;
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

    [Theory]
    [InlineData("ScrapingService")]
    [InlineData("IWebhookDispatchService")]
    [InlineData("INtfyService")]
    public void UserUrlClient_UsesThePublicAddressHandler(string clientName)
    {
        // Every client that fetches a user-chosen URL must connect through PublicAddressHandler.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(BuildConfig(new Dictionary<string, string?> { ["POSTGRES_CONNECTION"] = "Host=unused" }));
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var primary = handler.Should().BeOfType<SocketsHttpHandler>().Which;
        primary.ConnectCallback.Should().NotBeNull();
        primary.UseProxy.Should().BeFalse();
    }

    private static ServiceProvider BuildProviderWithAllowedNetworks(string allowedNetworks)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["POSTGRES_CONNECTION"] = "Host=unused",
            [WebhookAddressPolicy.ConfigKey] = allowedNetworks
        }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WebhookAllowedNetworks_DoNotReopenThemForTheScraper()
    {
        // The allowlist is for webhooks only. The scraper fetches URLs any account can choose, so it
        // must keep refusing private networks whatever the operator lists.
        using var provider = BuildProviderWithAllowedNetworks("10.0.0.0/8");
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ScrapingService");

        var act = () => client.GetAsync("http://10.255.255.1/", TestContext.Current.CancellationToken);

        PublicAddressHandler.IsBlockedDestination((await act.Should().ThrowAsync<HttpRequestException>()).Which)
            .Should().BeTrue();
    }

    [Fact]
    public async Task WebhookAllowedNetworks_ReopenThemForTheWebhookClient()
    {
        using var provider = BuildProviderWithAllowedNetworks("10.0.0.0/8");
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("IWebhookDispatchService");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        // Nothing listens there: the connect attempt fails or times out, but it is not refused.
        var act = () => client.GetAsync("http://10.255.255.1/", cts.Token);

        PublicAddressHandler.IsBlockedDestination((await act.Should().ThrowAsync<Exception>()).Which)
            .Should().BeFalse();
    }

    [Fact]
    public void AddInfrastructure_WithAllowedNetworkOutsidePrivateRanges_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            [WebhookAddressPolicy.ConfigKey] = "169.254.0.0/16"
        }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*169.254.0.0/16*");
    }

    [Fact]
    public void UpstreamProxy_InvalidConfiguration_StopsStartup()
    {
        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            [UpstreamProxy.UrlKey] = "http://proxy.test:8080"
        }));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{UpstreamProxy.DomainsKey}*");
    }

    [Fact]
    public void UpstreamProxy_IsRegisteredFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["POSTGRES_CONNECTION"] = "Host=unused",
            [UpstreamProxy.UrlKey] = "socks5://proxy.test:1080",
            [UpstreamProxy.DomainsKey] = "shop.test"
        }));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<UpstreamProxy>().Routes("www.shop.test").Should().BeTrue();
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
        // Registered after AddInfrastructure so it also replaces the per-client primary handlers
        // (UsePublicAddressesOnly); ConfigureHttpClientDefaults runs first and would lose to them.
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new StubHandler()));
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
