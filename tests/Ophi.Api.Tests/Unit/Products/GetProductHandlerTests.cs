using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Products;

public class GetProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetProduct.Handler _handler;
    private readonly Guid _testUserId;

    public GetProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetProduct.Handler(_dbContext, TimeProvider.System, NullLogger<GetProduct.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        // Seed test user for FK constraints
        var testUser = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(testUser);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_WithValidProduct_ReturnsProductDetails()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.ImageUrl = "https://example.com/image.jpg";
        product.CurrentPrice = 99.99m;
        product.PreviousPrice = 120.00m;
        product.Currency = "EUR";
        product.Status = ProductStatus.Active;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(product.Id);
        result.Name.Should().Be("Test Product");
        result.Url.Should().Be("https://example.com/product");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
        result.CurrentPrice.Should().Be(99.99m);
        result.PreviousPrice.Should().Be(120.00m);
        result.Currency.Should().Be("EUR");
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var query = new GetProduct.Query(Guid.NewGuid(), _testUserId);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithOtherUsersProduct_ThrowsNotFoundException()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUser = new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(otherUser);

        var product = CreateProduct("Other's Product", "https://example.com/other");
        product.UserId = otherUserId;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithPriceHistory_CalculatesStatistics()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 100.00m;
        _dbContext.Products.Add(product);

        // Add price history
        var pricePoints = new[]
        {
            new PricePoint { ProductId = product.Id, Price = 80.00m, RecordedAt = DateTime.UtcNow.AddDays(-7) },
            new PricePoint { ProductId = product.Id, Price = 90.00m, RecordedAt = DateTime.UtcNow.AddDays(-5) },
            new PricePoint { ProductId = product.Id, Price = 120.00m, RecordedAt = DateTime.UtcNow.AddDays(-3) },
            new PricePoint { ProductId = product.Id, Price = 100.00m, RecordedAt = DateTime.UtcNow.AddDays(-1) }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Should().NotBeNull();
        result.Statistics.Min.Should().Be(80.00m);
        result.Statistics.Max.Should().Be(120.00m);
        result.Statistics.Average.Should().Be(97.50m); // (80 + 90 + 120 + 100) / 4
        result.Statistics.Current.Should().Be(100.00m);
    }

    [Fact]
    public async Task Handle_WithNoPriceHistory_ReturnsZeroStatistics()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 99.99m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Should().NotBeNull();
        result.Statistics.Min.Should().Be(0);
        result.Statistics.Max.Should().Be(0);
        result.Statistics.Average.Should().Be(0);
        result.Statistics.Current.Should().Be(99.99m);
    }

    [Fact]
    public async Task Handle_WithAlerts_ReturnsAlertsList()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 100.00m;
        _dbContext.Products.Add(product);

        var alert1 = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = _testUserId,
            TargetPrice = 80.00m,
            ReferencePrice = 100.00m,
            Condition = AlertCondition.Below,
            IsActive = true,
            LastTriggeredAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var alert2 = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = _testUserId,
            TargetPrice = 90.00m,
            ReferencePrice = 100.00m,
            Condition = AlertCondition.PercentDrop,
            IsActive = false,
            LastTriggeredAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Alerts.AddRange(alert1, alert2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Alerts.Should().HaveCount(2);
        result.Alerts.Should().Contain(a => a.TargetPrice == 80.00m && a.Condition == "below" && a.Active);
        result.Alerts.Should().Contain(a => a.TargetPrice == 90.00m && a.Condition == "percentDrop" && !a.Active);
    }

    [Fact]
    public async Task Handle_WithNoAlerts_ReturnsEmptyAlertsList()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPriceChange_CalculatesPriceChangePercentage()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 80.00m;
        product.PreviousPrice = 100.00m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.PriceChange.Should().Be(-20.00m); // (80 - 100) / 100 * 100 = -20%
    }

    [Fact]
    public async Task Handle_WithNoPreviousPrice_ReturnsNullPriceChange()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 99.99m;
        product.PreviousPrice = null;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.PriceChange.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithComparisonGroupId_ReturnsComparisonGroupId()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var group = new ComparisonGroup
        {
            Id = groupId,
            Name = "Test Group",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.ComparisonGroups.Add(group);

        var product = CreateProduct("Test Product", "https://example.com/product");
        product.ComparisonGroupId = groupId;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.ComparisonGroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task Handle_StatisticsUsesLast90DaysOnly()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 100.00m;
        _dbContext.Products.Add(product);

        // Add price points - some within 90 days, some outside
        var pricePoints = new[]
        {
            new PricePoint { ProductId = product.Id, Price = 50.00m, RecordedAt = DateTime.UtcNow.AddDays(-100) }, // Outside 90 days
            new PricePoint { ProductId = product.Id, Price = 80.00m, RecordedAt = DateTime.UtcNow.AddDays(-30) },  // Within 90 days
            new PricePoint { ProductId = product.Id, Price = 100.00m, RecordedAt = DateTime.UtcNow.AddDays(-1) }   // Within 90 days
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert - should only include the last 90 days prices (80 and 100)
        result.Statistics.Min.Should().Be(80.00m);
        result.Statistics.Max.Should().Be(100.00m);
        result.Statistics.Average.Should().Be(90.00m); // (80 + 100) / 2
    }

    [Fact]
    public async Task Handle_ProductWithSingleCurrency_ReturnsFalseForMismatch()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Multi-URL Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(
            new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = "https://store-a.com/product",
                Currency = "USD"
            },
            new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = "https://store-b.com/product",
                Currency = "USD"
            }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.HasCurrencyMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ProductWithMixedCurrencies_ReturnsTrueForMismatch()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Multi-Currency Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(
            new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = "https://us-store.com/product",
                Currency = "USD",
                LastCheckedAt = DateTime.UtcNow
            },
            new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = "https://uk-store.com/product",
                Currency = "GBP",
                LastCheckedAt = DateTime.UtcNow
            }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.HasCurrencyMismatch.Should().BeTrue();
    }

    // --- Deal Score Tests ---

    [Fact]
    public async Task Handle_WithPriceHistory_ReturnsDealScore()
    {
        // Arrange
        var product = CreateProduct("Score Product", "https://example.com/score");
        product.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlId = _dbContext.ProductUrls.First(u => u.ProductId == product.Id).Id;
        // Declining prices: at the min
        for (var i = 0; i < 5; i++)
        {
            _dbContext.PricePoints.Add(new PricePoint
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductUrlId = urlId,
                Price = 100m - (i * 12.5m),
                Currency = "USD",
                RecordedAt = DateTime.UtcNow.AddDays(-4 + i)
            });
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — product at min with declining trend = high score
        result.DealScore.Should().NotBeNull();
        result.DealScore.Should().BeGreaterThanOrEqualTo(70);
    }

    [Fact]
    public async Task Handle_WithNoPriceHistory_ReturnsNullDealScore()
    {
        // Arrange
        var product = CreateProduct("No History Score", "https://example.com/nohistscore");
        product.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.DealScore.Should().BeNull();
    }

    // --- Affiliate URL Tests ---

    [Fact]
    public async Task Handle_WithAffiliatesEnabled_ReturnsAffiliateUrls()
    {
        // Arrange
        var product = CreateProduct("Affiliate Product", "https://example.com/product?id=1", storeId: "my-store");
        _dbContext.Products.Add(product);

        // Enable affiliates on user
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AffiliatesEnabled = true;

        // Add store config with affiliate codes
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            AffiliateParamName = "tag",
            AffiliateTag = "ophi-20"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Urls.Should().HaveCount(1);
        result.Urls[0].AffiliateUrl.Should().Contain("tag=ophi-20");
    }

    [Fact]
    public async Task Handle_WithAffiliatesDisabled_ReturnsNullAffiliateUrls()
    {
        // Arrange
        var product = CreateProduct("No Affiliate Product", "https://example.com/product", storeId: "my-store");
        _dbContext.Products.Add(product);

        // Disable affiliates on user
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AffiliatesEnabled = false;

        // Add store config with affiliate codes (should be ignored)
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            AffiliateParamName = "tag",
            AffiliateTag = "ophi-20"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Urls.Should().HaveCount(1);
        result.Urls[0].AffiliateUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNoAffiliateConfig_ReturnsNullAffiliateUrl()
    {
        // Arrange
        var product = CreateProduct("Product", "https://example.com/product", storeId: "unconfig-store");
        _dbContext.Products.Add(product);

        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AffiliatesEnabled = true;

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProduct.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Urls[0].AffiliateUrl.Should().BeNull();
    }

    private Product CreateProduct(string name, string url, string? storeId = null)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = url,
            Currency = "USD",
            StoreId = storeId
        });
        return product;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
