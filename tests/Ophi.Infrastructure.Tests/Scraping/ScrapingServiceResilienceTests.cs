using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using RichardSzalay.MockHttp;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// Resilience-focused tests for <see cref="ScrapingService"/>: cancellation, timeout, network
/// failures, and HTTP error classification. Complements the happy-path coverage in
/// <see cref="ScrapingServiceTests"/>.
/// </summary>
public class ScrapingServiceResilienceTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly ScrapingService _service;

    public ScrapingServiceResilienceTests()
    {
        var loggerMock = new Mock<ILogger<ScrapingService>>();
        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        IStoreConfigProvider configProvider = new CodeStoreConfigProvider();
        _service = new ScrapingService(httpClient, loggerMock.Object, configProvider);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenCancelled_RethrowsCancellation()
    {
        // A cancelled scrape (host shutdown, aborted request) is not a scrape failure: reporting
        // it as one budgets a restart against the URL's auto-pause count. It must escape to the
        // worker's retry rule (docs/agent-notes.md § Messaging), as PlaywrightScrapingService does.
        const string url = "https://example.com/slow";
        var slowResponse = new TaskCompletionSource<HttpResponseMessage>();
        _mockHttp.When(url).Respond(_ => slowResponse.Task);

        using var cts = new CancellationTokenSource();
        var scrapeTask = _service.ScrapeProductAsync(url, cancellationToken: cts.Token);

        await cts.CancelAsync();
        slowResponse.TrySetCanceled(cts.Token);

        var act = async () => await scrapeTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenTimedOutWithoutCancellation_ReturnsNetworkFailure()
    {
        // The other side of the filter: a timeout on a live token is a real scrape failure.
        const string url = "https://example.com/timeout";
        _mockHttp.When(url).Throw(new TaskCanceledException("timeout"));

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.NetworkError);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ScrapeErrorCategory.NotFound)]
    [InlineData(HttpStatusCode.Gone, ScrapeErrorCategory.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, ScrapeErrorCategory.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, ScrapeErrorCategory.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ScrapeErrorCategory.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, ScrapeErrorCategory.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ScrapeErrorCategory.ServerError)]
    public async Task ScrapeProductAsync_WithHttpErrorStatus_ClassifiesCorrectly(
        HttpStatusCode status, ScrapeErrorCategory expectedCategory)
    {
        const string url = "https://example.com/product";
        _mockHttp.When(url).Respond(status);

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(expectedCategory);
        result.HttpStatusCode.Should().Be((int)status);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithNetworkException_ReturnsNetworkErrorCategory()
    {
        const string url = "https://example.com/unreachable";
        _mockHttp.When(url).Throw(new HttpRequestException("Connection refused"));

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.NetworkError);
        result.Error.Should().Contain("Network error");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithEmptyResponseBody_ReturnsParseError()
    {
        // Empty 200 OK — fetch succeeds but parser has nothing to extract.
        const string url = "https://example.com/empty";
        _mockHttp.When(url).Respond("text/html", string.Empty);

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.ParseError);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithMalformedHtml_StillAttemptsExtraction()
    {
        // AngleSharp is tolerant; broken HTML still parses, just yields no price.
        const string url = "https://example.com/malformed";
        const string brokenHtml = "<html><body><div class='price'>not<a number</div></body>";
        _mockHttp.When(url).Respond("text/html", brokenHtml);

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Should reach the parser (not crash), produce a ParseError or success-with-no-price.
        result.Should().NotBeNull();
        result.ErrorCategory.Should().BeOneOf(ScrapeErrorCategory.ParseError, ScrapeErrorCategory.None);
    }
}
