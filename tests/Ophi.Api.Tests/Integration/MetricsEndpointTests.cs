using System.Net;
using FluentAssertions;
using Ophi.Infrastructure.Metrics;

namespace Ophi.Api.Tests.Integration;

public class MetricsEndpointTests(OphiWebApplicationFactory factory) : IsolatedIntegrationTest(factory), IClassFixture<OphiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Metrics_ReturnsOk()
    {
        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Prometheus text format includes standard process metrics
        content.Should().Contain("# HELP");
        content.Should().Contain("# TYPE");
    }

    [Fact]
    public async Task Metrics_IncludesCustomMetrics()
    {
        // Labeled metrics only appear after at least one observation
        AppMetrics.ScrapeDurationSeconds.WithLabels("test.example.com").Observe(0.1);

        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        content.Should().Contain("ophi_scrape_duration_seconds");
    }

    [Fact]
    public async Task Metrics_IncludesHttpMetrics()
    {
        // Make a request first to generate HTTP metrics
        await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        content.Should().Contain("http_request");
    }

    [Fact]
    public async Task Metrics_IncludesSecurityHeaders()
    {
        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }
}
