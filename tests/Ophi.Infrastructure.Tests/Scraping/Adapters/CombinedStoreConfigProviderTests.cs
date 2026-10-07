using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Scraping.Adapters;

public class CombinedStoreConfigProviderTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Ophi.Infrastructure.Persistence.OphiDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly CombinedStoreConfigProvider _provider;
    private readonly Guid _testUserId = Guid.NewGuid();

    public CombinedStoreConfigProviderTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        var codeProvider = new CodeStoreConfigProvider();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _provider = new CombinedStoreConfigProvider(_dbContext, codeProvider, _cache);

        // Create test user
        var testUser = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            PasswordHash = "hash",
            Name = "Test User"
        };
        _dbContext.Users.Add(testUser);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetAllConfigs_ReturnsBuiltInOnly()
    {
        // Act
        var configs = _provider.GetAllConfigs();

        // Assert
        configs.Should().NotBeEmpty();
        configs.Should().AllSatisfy(c => c.IsBuiltIn.Should().BeTrue());
        configs.Select(c => c.Id).Should().Contain(["amazon", "ebay"]);
    }

    [Theory]
    [InlineData("https://www.amazon.com/dp/B123", "amazon")]
    [InlineData("https://www.ebay.com/itm/123", "ebay")]
    public void GetConfigForUrl_MatchesBuiltIn(string url, string expectedStoreId)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(expectedStoreId);
    }

    [Theory]
    [InlineData("https://www.unknownstore.com/product")]
    [InlineData("https://example.com/item")]
    public void GetConfigForUrl_ReturnsNullForUnknown(string url)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public void GetGenericConfig_ReturnsCommonSelectors()
    {
        // Act
        var config = _provider.GetGenericConfig();

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be("generic");
        config.Selectors.PriceSelectors.Should().NotBeEmpty();
        config.Selectors.NameSelectors.Should().NotBeEmpty();
        config.Selectors.ImageSelectors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetConfigsForUserAsync_ReturnsUserConfigs()
    {
        // Arrange
        var userStore = CreateUserStoreConfig("mystore", "My Store", ["mystore.com"]);
        _dbContext.StoreConfigurations.Add(userStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // Act
        var configs = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);

        // Assert
        configs.Should().Contain(c => c.Id == "mystore");
        configs.First(c => c.Id == "mystore").IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task GetConfigsForUserAsync_MergesUserBeforeBuiltIn()
    {
        // Arrange
        var userStore = CreateUserStoreConfig("userstore", "User Store", ["userstore.com"]);
        _dbContext.StoreConfigurations.Add(userStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // Act
        var configs = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);

        // Assert
        var userStoreIndex = configs.ToList().FindIndex(c => c.Id == "userstore");
        var amazonIndex = configs.ToList().FindIndex(c => c.Id == "amazon");

        userStoreIndex.Should().BeLessThan(amazonIndex, "User configs should come before built-in configs");
    }

    [Fact]
    public async Task GetConfigsForUserAsync_CachesResults()
    {
        // Arrange
        var userStore = CreateUserStoreConfig("cachedstore", "Cached Store", ["cachedstore.com"]);
        _dbContext.StoreConfigurations.Add(userStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // First call - populates cache
        var firstResult = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);
        firstResult.Should().Contain(c => c.Id == "cachedstore");

        // Add another config (directly to DB, simulating external change)
        var anotherStore = CreateUserStoreConfig("anotherstore", "Another Store", ["anotherstore.com"]);
        _dbContext.StoreConfigurations.Add(anotherStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second call - should use cache (not see new store)
        var secondResult = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);

        // Assert - should NOT contain the new store because it's cached
        secondResult.Should().NotContain(c => c.Id == "anotherstore");
    }

    [Fact]
    public async Task GetConfigForUrlAsync_UserTakesPrecedence()
    {
        // Arrange - create user config with same domain pattern as built-in
        var userAmazonConfig = CreateUserStoreConfig(
            "amazon-custom",
            "My Amazon",
            ["amazon.com"],
            new StoreSelectorConfig
            {
                PriceSelectors = [".custom-price"],
                NameSelectors = [".custom-name"],
                ImageSelectors = [".custom-image"]
            });
        _dbContext.StoreConfigurations.Add(userAmazonConfig);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // Act
        var config = await _provider.GetConfigForUrlAsync("https://www.amazon.com/dp/B123", _testUserId, TestContext.Current.CancellationToken);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be("amazon-custom");
        config.IsBuiltIn.Should().BeFalse();
        config.Selectors.PriceSelectors.Should().Contain(".custom-price");
    }

    [Fact]
    public async Task GetConfigForUrlAsync_FallsBackToBuiltIn()
    {
        // Arrange - no user configs
        _provider.InvalidateCache(_testUserId);

        // Act
        var config = await _provider.GetConfigForUrlAsync("https://www.ebay.com/itm/123", _testUserId, TestContext.Current.CancellationToken);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be("ebay");
        config.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public async Task GetConfigForUrlAsync_ReturnsNull_WhenNoMatch()
    {
        // Act
        var config = await _provider.GetConfigForUrlAsync("https://www.unknown-store.com/product", _testUserId, TestContext.Current.CancellationToken);

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCache_CausesReload()
    {
        // Arrange
        var store = CreateUserStoreConfig("initial", "Initial Store", ["initial.com"]);
        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // First call - populates cache
        var firstResult = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);
        firstResult.Should().Contain(c => c.Id == "initial");

        // Add new config
        var newStore = CreateUserStoreConfig("newstore", "New Store", ["newstore.com"]);
        _dbContext.StoreConfigurations.Add(newStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Invalidate cache
        _provider.InvalidateCache(_testUserId);

        // Second call - should reload from DB
        var secondResult = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);

        // Assert - should now contain the new store
        secondResult.Should().Contain(c => c.Id == "newstore");
    }

    [Theory]
    [InlineData("https://AMAZON.COM/dp/B123", "amazon")]
    [InlineData("https://Amazon.Com/dp/B123", "amazon")]
    [InlineData("https://EBAY.com/itm/123", "ebay")]
    public void DomainMatching_IsCaseInsensitive(string url, string expectedStoreId)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(expectedStoreId);
    }

    [Theory]
    [InlineData("https://www.amazon.com/dp/B123", "amazon")]
    [InlineData("https://amazon.com/dp/B123", "amazon")]
    public void DomainMatching_StripsWwwPrefix(string url, string expectedStoreId)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(expectedStoreId);
    }

    [Theory]
    [InlineData("https://smile.amazon.com/dp/B123", "amazon")]
    [InlineData("https://music.amazon.com/product", "amazon")]
    public void DomainMatching_MatchesSubdomains(string url, string expectedStoreId)
    {
        // Act
        var config = _provider.GetConfigForUrl(url);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(expectedStoreId);
    }

    [Fact]
    public async Task DeserializesSelectorsJson()
    {
        // Arrange
        var selectors = new StoreSelectorConfig
        {
            PriceSelectors = [".price", "#cost"],
            NameSelectors = ["h1", ".title"],
            ImageSelectors = ["img.product", ".gallery img"],
            PriceRegexPatterns = [@"\$(\d+\.?\d*)"],
            ImageRegexPatterns = [@"src=""([^""]+\.jpg)"""]
        };

        var store = new StoreConfiguration
        {
            StoreId = "jsontest",
            Name = "JSON Test Store",
            UserId = _testUserId,
            DomainPatternsJson = "[\"jsontest.com\"]",
            SelectorsJson = JsonSerializer.Serialize(selectors)
        };

        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // Act
        var configs = await _provider.GetConfigsForUserAsync(_testUserId, TestContext.Current.CancellationToken);
        var config = configs.FirstOrDefault(c => c.Id == "jsontest");

        // Assert
        config.Should().NotBeNull();
        config.Selectors.PriceSelectors.Should().BeEquivalentTo([".price", "#cost"]);
        config.Selectors.NameSelectors.Should().BeEquivalentTo(["h1", ".title"]);
        config.Selectors.ImageSelectors.Should().BeEquivalentTo(["img.product", ".gallery img"]);
        config.Selectors.PriceRegexPatterns.Should().BeEquivalentTo([@"\$(\d+\.?\d*)"]);
        config.Selectors.ImageRegexPatterns.Should().BeEquivalentTo([@"src=""([^""]+\.jpg)"""]);
    }

    [Fact]
    public async Task DeserializesDomainPatternsJson()
    {
        // Arrange
        var store = new StoreConfiguration
        {
            StoreId = "multidomainstore",
            Name = "Multi Domain Store",
            UserId = _testUserId,
            DomainPatternsJson = "[\"domain1.com\", \"domain2.com\", \"domain3.net\"]",
            SelectorsJson = JsonSerializer.Serialize(new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"]
            })
        };

        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _provider.InvalidateCache(_testUserId);

        // Act
        var config = await _provider.GetConfigForUrlAsync("https://www.domain2.com/product", _testUserId, TestContext.Current.CancellationToken);

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be("multidomainstore");
        config.DomainPatterns.Should().BeEquivalentTo(["domain1.com", "domain2.com", "domain3.net"]);
    }

    private StoreConfiguration CreateUserStoreConfig(
        string storeId,
        string name,
        string[] domainPatterns,
        StoreSelectorConfig? selectors = null)
    {
        selectors ??= new StoreSelectorConfig
        {
            PriceSelectors = [".price"],
            NameSelectors = ["h1"],
            ImageSelectors = ["img"]
        };

        return new StoreConfiguration
        {
            StoreId = storeId,
            Name = name,
            UserId = _testUserId,
            DomainPatternsJson = JsonSerializer.Serialize(domainPatterns),
            SelectorsJson = JsonSerializer.Serialize(selectors)
        };
    }
}
