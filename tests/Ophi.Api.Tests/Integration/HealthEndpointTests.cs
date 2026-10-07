using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Ophi.Api.Tests.Integration;

public class HealthEndpointTests(OphiWebApplicationFactory factory) : IsolatedIntegrationTest(factory), IClassFixture<OphiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // --- Liveness ---

    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task HealthLive_IncludesUptimeAndVersion()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("uptime", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("version", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HealthLive_IncludesSecurityHeaders()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
    }

    // --- Readiness ---

    [Fact]
    public async Task HealthReady_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_IncludesAllChecks()
    {
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");

        var checks = doc.RootElement.GetProperty("checks");
        checks.TryGetProperty("database", out var db).Should().BeTrue();
        db.GetProperty("status").GetString().Should().Be("healthy");

        checks.TryGetProperty("wolverine", out var wolverine).Should().BeTrue();
        wolverine.GetProperty("status").GetString().Should().Be("healthy");

        checks.TryGetProperty("scraping", out var scraping).Should().BeTrue();
        scraping.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task HealthReady_IncludesUptimeAndVersion()
    {
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("uptime", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("version", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HealthReady_IncludesSecurityHeaders()
    {
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    // --- Legacy /health redirect ---

    [Fact]
    public async Task Health_LegacyEndpoint_StillWorks()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
    }
}
