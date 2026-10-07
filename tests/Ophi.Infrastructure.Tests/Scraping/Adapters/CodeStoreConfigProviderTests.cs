using FluentAssertions;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Tests.Scraping.Adapters;

public class CodeStoreConfigProviderTests
{
    private readonly CodeStoreConfigProvider _provider = new();

    [Fact]
    public void GetAllConfigs_ReturnsAllBuiltInStores()
    {
        // Act
        var configs = _provider.GetAllConfigs();

        // Assert
        configs.Should().HaveCountGreaterThanOrEqualTo(2);
        configs.Select(c => c.Id).Should().Contain(["amazon", "ebay"]);
    }

    [Theory]
    [InlineData("https://www.amazon.com/dp/B08N5WRWNW", "amazon")]
    [InlineData("https://amazon.com/product", "amazon")]
    [InlineData("https://www.amazon.co.uk/dp/B08N5WRWNW", "amazon")]
    [InlineData("https://www.amazon.de/dp/B08N5WRWNW", "amazon")]
    [InlineData("https://www.ebay.com/itm/123456789", "ebay")]
    [InlineData("https://ebay.co.uk/itm/123", "ebay")]
    public void GetConfigForUrl_WithKnownStoreUrl_ReturnsCorrectConfig(string url, string expectedStoreId)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(expectedStoreId);
    }

    [Theory]
    [InlineData("https://www.unknownstore.com/product")]
    [InlineData("https://example.com/product")]
    [InlineData("https://randomshop.net/item/123")]
    public void GetConfigForUrl_WithUnknownStoreUrl_ReturnsNull(string url)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public void GetConfigForUrl_WithInvalidUrl_ReturnsNull()
    {
        // Act
        var config = _provider.GetConfigForUrl("not-a-valid-url");

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public void GetGenericConfig_ReturnsGenericStore()
    {
        // Act
        var config = _provider.GetGenericConfig();

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be("generic");
        config.Name.Should().Be("Generic Store");
        config.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void GetGenericConfig_HasCommonSelectors()
    {
        // Act
        var config = _provider.GetGenericConfig();

        // Assert
        config.Selectors.PriceSelectors.Should().NotBeEmpty();
        config.Selectors.NameSelectors.Should().NotBeEmpty();
        config.Selectors.ImageSelectors.Should().NotBeEmpty();
    }

    [Fact]
    public void AmazonConfig_HasAmazonSpecificSelectors()
    {
        // Act
        var config = _provider.GetConfigForUrl("https://www.amazon.com/dp/B123");

        // Assert
        config.Should().NotBeNull();
        config.Selectors.PriceSelectors.Should().Contain(".a-price .a-offscreen");
        config.Selectors.NameSelectors.Should().Contain("#productTitle");
        config.Selectors.ImageSelectors.Should().Contain(s => s.Contains("landingImage"));
    }

    [Fact]
    public void AllBuiltInConfigs_HaveIsBuiltInSetToTrue()
    {
        // Act
        var configs = _provider.GetAllConfigs();

        // Assert
        configs.Should().AllSatisfy(c => c.IsBuiltIn.Should().BeTrue());
    }

    [Fact]
    public void AllConfigs_HaveRequiredSelectors()
    {
        // Act
        var configs = _provider.GetAllConfigs();

        // Assert
        configs.Should().AllSatisfy(c =>
        {
            c.Selectors.PriceSelectors.Should().NotBeEmpty($"{c.Name} should have price selectors");
            c.Selectors.NameSelectors.Should().NotBeEmpty($"{c.Name} should have name selectors");
            c.Selectors.ImageSelectors.Should().NotBeEmpty($"{c.Name} should have image selectors");
        });
    }
}
