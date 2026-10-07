using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Phase 6.8 — Validates that a negative-path scraping mock is wired into the integration
/// factory. The default <see cref="OphiWebApplicationFactory"/> uses
/// <see cref="OphiWebApplicationFactory.MockScrapingService"/> which always succeeds, leaving
/// the failure pathway untested at the integration layer. <see cref="FailingScrapeFactory"/>
/// provides a counterpart for future negative-path coverage.
///
/// The handler-level failure path is fully covered by unit tests in
/// <c>tests/Ophi.Infrastructure.Tests/Handlers/ScrapeNewProductHandlerTests.cs</c> — this test
/// is the missing piece that says "the integration test rig CAN exercise the failure path
/// when needed".
/// </summary>
public class FailingScrapeEndpointTests : IClassFixture<FailingScrapeFactory>
{
    private readonly FailingScrapeFactory _factory;

    public FailingScrapeEndpointTests(FailingScrapeFactory factory) => _factory = factory;

    [Fact]
    public async Task FailingScrapeFactory_InjectsFailingScrapingService()
    {
        using var scope = _factory.Services.CreateScope();
        var scraper = scope.ServiceProvider.GetRequiredService<IScrapingService>();

        scraper.Should().BeOfType<FailingScrapingService>(
            "the negative-path factory must swap in the failing mock so tests can exercise the error pathway");
    }

    [Fact]
    public async Task FailingScrapingService_ReturnsParseErrorResult()
    {
        using var scope = _factory.Services.CreateScope();
        var scraper = scope.ServiceProvider.GetRequiredService<IScrapingService>();

        var result = await scraper.ScrapeProductAsync(
            "https://anything.example.com",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.ParseError);
        result.Error.Should().Contain("price");
    }
}
