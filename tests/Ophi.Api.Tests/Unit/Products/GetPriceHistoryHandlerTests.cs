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

public class GetPriceHistoryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetPriceHistory.Handler _handler;
    private readonly Guid _testUserId;

    public GetPriceHistoryHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetPriceHistory.Handler(_dbContext, TimeProvider.System, NullLogger<GetPriceHistory.Handler>.Instance);
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
    public async Task Handle_WithValidProduct_ReturnsProductInfo()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.ProductId.Should().Be(product.Id);
        result.ProductName.Should().Be("Test Product");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var query = new GetPriceHistory.Query(Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithProductBelongingToDifferentUser_ThrowsNotFoundException()
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

        var otherUsersProduct = CreateProduct("Other's Product");
        otherUsersProduct.UserId = otherUserId;
        _dbContext.Products.Add(otherUsersProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(otherUsersProduct.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithNoPricePoints_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPricePoints_ReturnsHistoryOrderedByDate()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 90m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 85m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.History.Should().HaveCount(3);
        result.History[0].Price.Should().Be(100m);
        result.History[1].Price.Should().Be(90m);
        result.History[2].Price.Should().Be(85m);
    }

    [Fact]
    public async Task Handle_WithDaysParameter_FiltersToRecentHistory()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-60) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 90m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-10) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 85m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 30);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.History.Should().HaveCount(2);
        result.History.Should().NotContain(h => h.Price == 100m);
    }

    [Fact]
    public async Task Handle_WithPricePoints_CalculatesMinStatistic()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 85m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 85m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Min.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_WithPricePoints_CalculatesMaxStatistic()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 85m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 85m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Max.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_WithPricePoints_CalculatesAverageStatistic()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 85m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 85m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Average.Should().Be(78.33m);
    }

    [Fact]
    public async Task Handle_WithPricePoints_ReturnsCurrentPrice()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 79.99m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Current.Should().Be(79.99m);
    }

    [Fact]
    public async Task Handle_WithNoPricePoints_ReturnsZeroStatistics()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Statistics.Min.Should().Be(0);
        result.Statistics.Max.Should().Be(0);
        result.Statistics.Average.Should().Be(0);
        result.Statistics.Current.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_DefaultsToThirtyDays()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-35) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 90m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-25) }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — the 35-day-old point (100m) is excluded; the in-window point (90m) is
        // synthesized into a flat line so the chart can render.
        result.History.Should().OnlyContain(h => h.Price == 90m);
        result.History.Should().NotContain(h => h.Price == 100m);
    }

    [Fact]
    public async Task Handle_WithSinglePointInWindow_SynthesizesFlatLine()
    {
        // Arrange — a stable-price product scraped once inside the window has exactly one point.
        var product = CreateProduct("Stable Price Product");
        product.CurrentPrice = 16.72m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pricePoint = new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Price = 16.72m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddDays(-1)
        };
        _dbContext.PricePoints.Add(pricePoint);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — two points at the same price so the chart draws a flat line instead of
        // showing "not enough data yet".
        result.History.Should().HaveCount(2);
        result.History.Should().OnlyContain(h => h.Price == 16.72m);
        result.History[0].Date.Should().BeBefore(result.History[1].Date);
    }

    [Fact]
    public async Task Handle_WithMultipleUrls_IncludesCurrencyInUrlHistory()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Multi URL Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var url1 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.com/p", Currency = "USD" };
        var url2 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.co.uk/p", Currency = "GBP" };
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.Products.Add(product);

        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url1.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url2.Id, Price = 80m, Currency = "GBP", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.UrlHistories.Should().NotBeNull();
        result.UrlHistories.Should().HaveCount(2);
        result.UrlHistories![0].Currency.Should().Be("USD");
        result.UrlHistories![1].Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task Handle_WithMixedCurrencies_SetsHasCurrencyMismatchTrue()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Mixed Currency Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var url1 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.com/p", Currency = "USD" };
        var url2 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.co.uk/p", Currency = "GBP" };
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.HasCurrencyMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSameCurrencies_SetsHasCurrencyMismatchFalse()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Same Currency Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var url1 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.com/p1", Currency = "USD" };
        var url2 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.com/p2", Currency = "USD" };
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.HasCurrencyMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithNoPointsInWindow_CarriesForwardLastKnownPrice()
    {
        // Arrange
        var product = CreateProduct("Stable Price Product");
        product.CurrentPrice = 39.90m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Only price point is 60 days ago — outside the 7-day window
        var pricePoint = new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Price = 39.90m,
            Currency = "EUR",
            RecordedAt = DateTime.UtcNow.AddDays(-60)
        };
        _dbContext.PricePoints.Add(pricePoint);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — should synthesize two points so the chart renders a flat line
        result.History.Should().HaveCount(2);
        result.History[0].Price.Should().Be(39.90m);
        result.History[1].Price.Should().Be(39.90m);
        result.Statistics.Min.Should().Be(39.90m);
        result.Statistics.Max.Should().Be(39.90m);
    }

    [Fact]
    public async Task Handle_WithNoPointsAtAll_ReturnsEmptyHistory()
    {
        // Arrange — no price points exist anywhere (truly new product)
        var product = CreateProduct("Brand New Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — no carry-forward possible, stays empty
        result.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithSingleUrl_HasCurrencyMismatchIsFalse()
    {
        // Arrange
        var product = CreateProduct("Single URL Product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.HasCurrencyMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OnShortWindow_KeepsMultipleSameDayScrapes()
    {
        // Arrange — three scrapes on the same calendar day at different times/prices.
        var product = CreateProduct("Intraday Product");
        product.CurrentPrice = 18m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var day = DateTime.UtcNow.Date.AddDays(-1);
        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 20m, Currency = "USD", RecordedAt = day.AddHours(9) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 19m, Currency = "USD", RecordedAt = day.AddHours(12) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 18m, Currency = "USD", RecordedAt = day.AddHours(15) }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — on the 7d view intraday points are preserved, not collapsed to one/day.
        result.History.Should().HaveCount(3);
        result.History.Select(h => h.Price).Should().ContainInOrder(20m, 19m, 18m);
    }

    [Fact]
    public async Task Handle_OnLongWindow_CollapsesSameDayScrapesToOnePoint()
    {
        // Arrange — same intraday scrapes, but viewed on a long (90d) window.
        var product = CreateProduct("Intraday Product");
        product.CurrentPrice = 18m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var day = DateTime.UtcNow.Date.AddDays(-1);
        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 20m, Currency = "USD", RecordedAt = day.AddHours(9) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 19m, Currency = "USD", RecordedAt = day.AddHours(12) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 18m, Currency = "USD", RecordedAt = day.AddHours(15) }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 90);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — collapses to the last scrape of the day (18), synthesized into a flat line.
        result.History.Should().HaveCount(2);
        result.History.Should().OnlyContain(h => h.Price == 18m);
    }

    [Fact]
    public async Task Handle_WithMultipleUrls_StablePrices_SynthesizesFlatLinePerUrl()
    {
        // Arrange — two stores, each scraped once at a stable price (same currency).
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Two Stable Stores",
            Currency = "EUR",
            Status = ProductStatus.Active
        };
        var url1 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.es/p", Currency = "EUR" };
        var url2 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://worten.pt/p", Currency = "EUR" };
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url1.Id, Price = 294.78m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddHours(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url2.Id, Price = 279.99m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddHours(-2) }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — each per-URL series must have >= 2 points so the multi-line chart draws a
        // flat line per store instead of an empty canvas.
        result.UrlHistories.Should().NotBeNull();
        result.UrlHistories.Should().HaveCount(2);
        result.UrlHistories!.Should().OnlyContain(uh => uh.History.Count == 2);
        result.UrlHistories!.First(uh => uh.Url.Contains("amazon")).History.Should().OnlyContain(h => h.Price == 294.78m);
        result.UrlHistories!.First(uh => uh.Url.Contains("worten")).History.Should().OnlyContain(h => h.Price == 279.99m);
    }

    [Fact]
    public async Task Handle_WithUrlStableLongerThanWindow_CarriesItsPriceForward()
    {
        // Arrange — the real shape of the bug. Price history is change-only
        // (RecordPriceHistoryHandler skips unchanged prices), so a store whose price hasn't moved
        // in months has NO points inside a 7-day window. Its sibling does, so the product-level
        // carry-forward doesn't fire either. The store used to come back with an empty series:
        // invisible on the chart but still occupying a legend entry, and — because the only
        // remaining series was constant — Chart.js ballooned the y-axis to ±5% of that value,
        // putting the missing store's real price below the axis floor.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "One Stable, One Moving",
            Currency = "EUR",
            Status = ProductStatus.Active
        };
        var moving = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.es/p", Currency = "EUR" };
        var stable = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://worten.pt/p", Currency = "EUR" };
        _dbContext.ProductUrls.AddRange(moving, stable);
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.AddRange(
            // In window.
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = moving.Id, Price = 294.78m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            // Well outside it — and an older one, to prove the LATEST prior point is the one used.
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = stable.Id, Price = 310.00m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddDays(-80) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = stable.Id, Price = 279.99m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddDays(-55) }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — the stable store draws a flat line at its last known price, not an empty series.
        result.UrlHistories.Should().NotBeNull();
        var stableSeries = result.UrlHistories!.First(uh => uh.Url.Contains("worten"));
        stableSeries.History.Should().HaveCountGreaterThanOrEqualTo(2);
        stableSeries.History.Should().OnlyContain(h => h.Price == 279.99m);
        stableSeries.Currency.Should().Be("EUR");

        result.UrlHistories!.First(uh => uh.Url.Contains("amazon")).History
            .Should().OnlyContain(h => h.Price == 294.78m);
    }

    [Fact]
    public async Task Handle_WithUrlThatHasNeverBeenScraped_LeavesItsSeriesEmpty()
    {
        // Guard on the fix above: carry-forward reconstructs a price that was genuinely held. A URL
        // with no points at ALL has no price to hold, so inventing one would be fabrication.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "One Scraped, One Never",
            Currency = "EUR",
            Status = ProductStatus.Active
        };
        var scraped = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://amazon.es/p", Currency = "EUR" };
        var never = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://worten.pt/p", Currency = "EUR" };
        _dbContext.ProductUrls.AddRange(scraped, never);
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.Add(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = scraped.Id, Price = 294.78m, Currency = "EUR", RecordedAt = DateTime.UtcNow.AddDays(-1) });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPriceHistory.Query(product.Id, _testUserId, Days: 7);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.UrlHistories!.First(uh => uh.Url.Contains("worten")).History.Should().BeEmpty();
    }

    private Product CreateProduct(string name)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        return product;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
