using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Moq;
using Ophi.Api.Features.Stores;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class GetStoresHandlerTests
{
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly GetStores.Handler _handler;
    private readonly Guid _testUserId = Guid.NewGuid();

    public GetStoresHandlerTests()
    {
        _handler = new GetStores.Handler(_configProviderMock.Object, NullLogger<GetStores.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithStoresForUser_ReturnsStores()
    {
        // Arrange
        var configs = new List<StoreConfig>
        {
            new()
            {
                Id = "amazon",
                Name = "Amazon",
                DomainPatterns = ["amazon.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = [".name"],
                    ImageSelectors = [".img"],
                },
                IsBuiltIn = true,
                EntityId = null,
            },
            new()
            {
                Id = "my-store",
                Name = "My Custom Store",
                DomainPatterns = ["mystore.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".custom-price"],
                    NameSelectors = [".custom-name"],
                    ImageSelectors = [".custom-img"],
                },
                IsBuiltIn = false,
                EntityId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
            },
        };

        _configProviderMock
            .Setup(p => p.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        var query = new GetStores.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.Items[0].StoreId.Should().Be("amazon");
        result.Items[0].Name.Should().Be("Amazon");
        result.Items[0].IsBuiltIn.Should().BeTrue();
        result.Items[1].StoreId.Should().Be("my-store");
        result.Items[1].Name.Should().Be("My Custom Store");
        result.Items[1].IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithNoStores_ReturnsEmptyList()
    {
        // Arrange
        _configProviderMock
            .Setup(p => p.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig>());

        var query = new GetStores.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithSearchQuery_FiltersStoresByName()
    {
        // Arrange
        var configs = new List<StoreConfig>
        {
            new()
            {
                Id = "amazon",
                Name = "Amazon",
                DomainPatterns = ["amazon.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = [".name"],
                    ImageSelectors = [".img"],
                },
                IsBuiltIn = true,
            },
            new()
            {
                Id = "ebay",
                Name = "eBay",
                DomainPatterns = ["ebay.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = [".name"],
                    ImageSelectors = [".img"],
                },
                IsBuiltIn = true,
            },
        };

        _configProviderMock
            .Setup(p => p.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        var query = new GetStores.Query(_testUserId, "amaz");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Total.Should().Be(1);
        result.Items[0].StoreId.Should().Be("amazon");
    }

    [Fact]
    public async Task Handle_WithDifferentUser_DoesNotReturnOtherUsersStores()
    {
        // Arrange — configProvider scopes by userId, so we set up empty for our user
        var otherUserId = Guid.NewGuid();

        _configProviderMock
            .Setup(p => p.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig>());

        _configProviderMock
            .Setup(p => p.GetConfigsForUserAsync(otherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig>
            {
                new()
                {
                    Id = "other-store",
                    Name = "Other Store",
                    DomainPatterns = ["other.com"],
                    Selectors = new StoreSelectorConfig
                    {
                        PriceSelectors = [".price"],
                        NameSelectors = [".name"],
                        ImageSelectors = [".img"],
                    },
                    IsBuiltIn = false,
                    EntityId = Guid.NewGuid(),
                },
            });

        var query = new GetStores.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        _configProviderMock.Verify(
            p => p.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
