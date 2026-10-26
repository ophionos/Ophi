using FluentAssertions;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class SiteHostTests
{
    [Theory]
    [InlineData("WWW.Amazon.ES", "amazon.es")]
    [InlineData("shop.example", "shop.example")]
    [InlineData("www2.shop.example", "www2.shop.example")]
    public void Normalize_LowercasesAndStripsLeadingWww(string host, string expected)
    {
        SiteHost.Normalize(host).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.amazon.es/dp/B000TEST01", "amazon.es")]
    [InlineData("http://Shop.Example:8080/item?x=1", "shop.example")]
    public void Of_ReturnsTheNormalizedHostOfAUrl(string url, string expected)
    {
        SiteHost.Of(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public void Of_WithNoAbsoluteUrl_ReturnsNull(string url)
    {
        SiteHost.Of(url).Should().BeNull();
    }
}
