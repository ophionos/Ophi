using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class ProductUrlKeyTests
{
    [Theory]
    // Generic tracking parameters are dropped on every host.
    [InlineData("https://shop.example/p/1?utm_source=news&utm_medium=email", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1?fbclid=abc", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1?gclid=abc&msclkid=def", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1?srsltid=abc", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1?UTM_Campaign=x", "https://shop.example/p/1")]
    // Fragment, host case, www., scheme, trailing slash and parameter order do not matter.
    [InlineData("https://shop.example/p/1#reviews", "https://shop.example/p/1")]
    [InlineData("https://WWW.Shop.Example/p/1", "https://shop.example/p/1")]
    [InlineData("http://shop.example/p/1", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1/", "https://shop.example/p/1")]
    [InlineData("https://shop.example/p/1?b=2&a=1", "https://shop.example/p/1?a=1&b=2")]
    // Amazon: the /ref= path segment and Amazon-only tracking keys are dropped.
    [InlineData("https://www.amazon.com/dp/B000123/ref=sr_1_3?qid=1&sr=8-3&keywords=mug", "https://amazon.com/dp/B000123")]
    [InlineData("https://www.amazon.co.uk/dp/B000123?pd_rd_w=x&pf_rd_p=y&tag=aff-21", "https://amazon.co.uk/dp/B000123")]
    public void For_UrlsThatDifferOnlyByNoise_HaveTheSameKey(string noisy, string clean)
    {
        ProductUrlKey.For(noisy).Should().Be(ProductUrlKey.For(clean));
    }

    [Theory]
    // A query parameter that selects the variant or SKU must survive: dropping it tracks another price.
    [InlineData("https://shop.example/p/1?variant=42", "https://shop.example/p/1?variant=43")]
    [InlineData("https://shop.example/p/1?variant=42", "https://shop.example/p/1")]
    [InlineData("https://www.amazon.com/dp/B000123?th=1&psc=1", "https://www.amazon.com/dp/B000123")]
    [InlineData("https://www.amazon.com/dp/B000123?smid=SELLER1", "https://www.amazon.com/dp/B000123?smid=SELLER2")]
    // Amazon-only keys are kept on other hosts, where they may mean something.
    [InlineData("https://shop.example/search?keywords=mug", "https://shop.example/search?keywords=tea")]
    [InlineData("https://shop.example/p/ref=1", "https://shop.example/p/ref=2")]
    // Path case and non-default ports are significant.
    [InlineData("https://shop.example/P/ABC", "https://shop.example/p/abc")]
    [InlineData("https://shop.example:8443/p/1", "https://shop.example/p/1")]
    // Different hosts are different products.
    [InlineData("https://shop.example/p/1", "https://other.example/p/1")]
    public void For_UrlsForDifferentProducts_HaveDifferentKeys(string a, string b)
    {
        ProductUrlKey.For(a).Should().NotBe(ProductUrlKey.For(b));
    }

    [Fact]
    public void For_NotAnAbsoluteHttpUrl_ReturnsTheTrimmedInput()
    {
        ProductUrlKey.For("  not a url ").Should().Be("not a url");
    }
}
