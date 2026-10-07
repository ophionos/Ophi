using FluentAssertions;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// Covers the URL → <c>StoreConfig</c> resolution that decides which selectors a scrape uses. This
/// provider had no tests at all, which is why issue #131 named coverage here as the prerequisite for
/// touching the built-in store set: a domain-matching mistake silently downgrades a store to the
/// generic selectors, and a generic match on a wrong price looks exactly like a working scrape.
/// </summary>
public class CodeStoreConfigProviderTests
{
    private readonly CodeStoreConfigProvider _provider = new();

    [Theory]
    [InlineData("https://www.amazon.com/dp/B01234", "amazon")]
    [InlineData("https://amazon.co.uk/dp/B01234", "amazon")]
    [InlineData("https://www.amazon.de/dp/B01234", "amazon")]
    [InlineData("https://www.ebay.com/itm/123", "ebay")]
    [InlineData("https://ebay.co.uk/itm/123", "ebay")]
    public void GetConfigForUrl_MatchesABuiltInStore(string url, string expectedId)
    {
        _provider.GetConfigForUrl(url)!.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GetConfigForUrl_MatchesARegionalSubdomain()
    {
        // The matcher accepts host == pattern or host ending in ".{pattern}", so smile.amazon.com
        // resolves to Amazon rather than falling through to the generic selectors.
        _provider.GetConfigForUrl("https://smile.amazon.com/dp/B01234")!.Id.Should().Be("amazon");
    }

    [Theory]
    [InlineData("https://www.worten.pt/produtos/123")]
    [InlineData("https://www.walmart.com/ip/123")]
    public void GetConfigForUrl_WithNoBuiltInStore_ReturnsNull(string url)
    {
        // Null means "no tuned config" and the caller falls back to GetGenericConfig. Walmart is in
        // here on purpose: there is no WalmartConfig, and #131 was filed because the docs and UI
        // claimed otherwise. If someone adds one, this line fails and they must update the claim
        // deliberately rather than leave the two out of step again.
        _provider.GetConfigForUrl(url).Should().BeNull();
    }

    [Fact]
    public void GetConfigForUrl_DoesNotMatchALookalikeDomain()
    {
        // "notamazon.com" must not match "amazon.com". The matcher requires a dot before the
        // pattern, so a suffix that is not a real subdomain boundary is rejected — otherwise a
        // hostile or coincidental domain would inherit another store's selectors.
        _provider.GetConfigForUrl("https://notamazon.com/dp/B01234").Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void GetConfigForUrl_WithAnUnparseableUrl_ReturnsNull(string url)
    {
        _provider.GetConfigForUrl(url).Should().BeNull();
    }

    [Fact]
    public void GetAllConfigs_ReturnsExactlyTheStoresThatExist()
    {
        // The one assertion that pins the claim #131 was about. Docs, the landing page and the
        // onboarding tour all name the built-in stores; if this set changes, that copy has to change
        // with it, and a failing test here is the reminder.
        _provider.GetAllConfigs().Select(c => c.Id).Should().BeEquivalentTo(["amazon", "ebay"]);
    }

    [Fact]
    public void GenericConfig_IsAvailableAndCarriesTheCommonSelectors()
    {
        var generic = _provider.GetGenericConfig();

        generic.Id.Should().Be("generic");
        generic.DomainPatterns.Should().BeEmpty("the generic config is the fallback, not a match");
        generic.Selectors.PriceSelectors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetConfigForUrlAsync_IgnoresTheUser_AndMatchesTheBuiltInStore()
    {
        // This provider is built-ins only; per-user configs live in CombinedStoreConfigProvider.
        // The async overload exists to satisfy the interface and must not diverge from the sync one.
        var sync = _provider.GetConfigForUrl("https://www.amazon.com/dp/B01234");
        var async = await _provider.GetConfigForUrlAsync(
            "https://www.amazon.com/dp/B01234", Guid.NewGuid(), TestContext.Current.CancellationToken);

        async!.Id.Should().Be(sync!.Id);
    }
}
