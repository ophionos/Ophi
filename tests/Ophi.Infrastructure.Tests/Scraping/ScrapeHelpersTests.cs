using System.Text.RegularExpressions;
using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class ScrapeHelpersTests
{
    [Fact]
    public void GetOrCreateRegex_AppliesAMatchTimeout()
    {
        // PriceRegexPatterns are user-authored (CreateStore/UpdateStore/ImportStore) and run against
        // a full rendered DOM. Without a match timeout a catastrophically-backtracking pattern pins
        // a worker thread indefinitely, and the RegexMatchTimeoutException handlers in both scraping
        // services become unreachable dead code.
        var regex = ScrapeHelpers.GetOrCreateRegex(@"price:\s*(\d+)");

        regex.MatchTimeout.Should().NotBe(Regex.InfiniteMatchTimeout);
        regex.MatchTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetOrCreateRegex_CachesByPattern()
    {
        var first = ScrapeHelpers.GetOrCreateRegex(@"cached-(\d+)");
        var second = ScrapeHelpers.GetOrCreateRegex(@"cached-(\d+)");

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetOrCreateRegex_DistinctPatterns_ReturnDistinctInstances()
    {
        var first = ScrapeHelpers.GetOrCreateRegex(@"alpha-(\d+)");
        var second = ScrapeHelpers.GetOrCreateRegex(@"beta-(\d+)");

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void GetOrCreateRegex_ManyDistinctPatterns_KeepsCacheBounded()
    {
        // Patterns are user-authored and each one is a compiled Regex held for the process lifetime.
        // Without a bound, every edited store config leaks its old patterns forever.
        for (var i = 0; i < ScrapeHelpers.MaxCachedRegexes + 10; i++)
            ScrapeHelpers.GetOrCreateRegex($@"bounded-{i}-(\d+)");

        ScrapeHelpers.CachedRegexCount.Should().BeLessThanOrEqualTo(ScrapeHelpers.MaxCachedRegexes);
    }

    [Theory]
    [InlineData("/img/a.png", "https://shop.example.com/p/1", "https://shop.example.com/img/a.png")]
    [InlineData("/img/a.png", "http://localhost:8080/p/1", "http://localhost:8080/img/a.png")]
    [InlineData("img/a.png", "https://shop.example.com/p/1", "https://shop.example.com/p/img/a.png")]
    [InlineData("//cdn.example.com/a.png", "http://shop.example.com/p/1", "http://cdn.example.com/a.png")]
    [InlineData("https://cdn.example.com/a.png", "https://shop.example.com/p/1", "https://cdn.example.com/a.png")]
    public void NormalizeImageUrl_ResolvesAgainstThePageUrl(string src, string baseUrl, string expected)
    {
        // Resolution follows the browser's rules (RFC 3986): the page's scheme and port are kept,
        // and a path without a leading slash is relative to the page's directory.
        ScrapeHelpers.NormalizeImageUrl(src, baseUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData("selector", "selector", null)]
    [InlineData("meta[itemprop=price]|content", "meta[itemprop=price]", "content")]
    public void ParseSelectorSpec_SplitsOnPipe(string spec, string expectedSelector, string? expectedAttribute)
    {
        var (selector, attribute) = ScrapeHelpers.ParseSelectorSpec(spec);

        selector.Should().Be(expectedSelector);
        attribute.Should().Be(expectedAttribute);
    }
}
