using FluentAssertions;
using Ophi.Infrastructure.Metrics;

namespace Ophi.Api.Tests.Unit.Metrics;

public class AppMetricsTests
{
    [Theory]
    [InlineData("https://www.amazon.com/dp/B01234", "www.amazon.com")]
    [InlineData("https://ebay.com/itm/12345", "ebay.com")]
    [InlineData("https://www.walmart.com/ip/Product/123", "www.walmart.com")]
    public void ExtractStore_ValidUrl_ReturnsHost(string url, string expected)
    {
        AppMetrics.ExtractStore(url).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void ExtractStore_InvalidUrl_ReturnsUnknown(string? url)
    {
        AppMetrics.ExtractStore(url).Should().Be("unknown");
    }

    [Fact]
    public void ScrapeTotal_IsRegistered()
    {
        AppMetrics.ScrapeTotal.Should().NotBeNull();
        AppMetrics.ScrapeTotal.WithLabels("test.com", "success").Inc();
        // No exception = registered correctly
    }

    [Fact]
    public void AlertFiredTotal_IsRegistered()
    {
        AppMetrics.AlertFiredTotal.Should().NotBeNull();
        AppMetrics.AlertFiredTotal.WithLabels("below").Inc();
    }

    [Fact]
    public void WebhookDispatchTotal_IsRegistered()
    {
        AppMetrics.WebhookDispatchTotal.Should().NotBeNull();
        AppMetrics.WebhookDispatchTotal.WithLabels("alert_fired", "success").Inc();
    }

    [Fact]
    public void ScrapeDurationSeconds_IsRegistered()
    {
        AppMetrics.ScrapeDurationSeconds.Should().NotBeNull();
        AppMetrics.ScrapeDurationSeconds.WithLabels("test.com").Observe(1.5);
    }

    [Fact]
    public void ProductPriceCurrent_IsRegistered()
    {
        AppMetrics.ProductPriceCurrent.Should().NotBeNull();
        AppMetrics.ProductPriceCurrent.WithLabels("id", "store").Set(9.99);
    }

    [Fact]
    public void ProductPriceLowest_IsRegistered()
    {
        AppMetrics.ProductPriceLowest.Should().NotBeNull();
        AppMetrics.ProductPriceLowest.WithLabels("id", "store").Set(4.99);
    }

    [Fact]
    public void ProductInfo_IsRegistered()
    {
        AppMetrics.ProductInfo.Should().NotBeNull();
        AppMetrics.ProductInfo.WithLabels("id", "name").Set(1);
    }
}
