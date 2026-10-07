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
