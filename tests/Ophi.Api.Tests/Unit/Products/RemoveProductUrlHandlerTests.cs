using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Products;

public class RemoveProductUrlHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly RemoveProductUrl.Handler _handler;
    private readonly Guid _testUserId;

    public RemoveProductUrlHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new RemoveProductUrl.Handler(_dbContext, NullLogger<RemoveProductUrl.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_WithTwoUrls_RemovesUrl()
    {
        // Arrange
        var (product, url1, url2) = CreateProductWithTwoUrls();
        var command = new RemoveProductUrl.Command(product.Id, url2.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var urls = await _dbContext.ProductUrls.Where(pu => pu.ProductId == product.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        urls.Should().HaveCount(1);
        urls[0].Id.Should().Be(url1.Id);
    }

    [Fact]
    public async Task Handle_WithLastUrl_ThrowsApiException()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);

        var url = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://amazon.com/product",
            Currency = "USD"
        };
        _dbContext.ProductUrls.Add(url);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductUrl.Command(product.Id, url.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Cannot remove the last URL from a product");
    }

    [Fact]
    public async Task Handle_RecalculatesCurrentPrice()
    {
        // Arrange
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 50.00m;
        url2.CurrentPrice = 30.00m;
        product.CurrentPrice = 30.00m; // Currently the min
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductUrl.Command(product.Id, url2.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(50.00m);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new RemoveProductUrl.Command(Guid.NewGuid(), Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithNonExistentUrl_ThrowsNotFoundException()
    {
        // Arrange
        var (product, _, _) = CreateProductWithTwoUrls();
        var command = new RemoveProductUrl.Command(product.Id, Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithOtherUsersProduct_ThrowsNotFoundException()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        });

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);

        var url1 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://a.com", Currency = "USD" };
        var url2 = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://b.com", Currency = "USD" };
        _dbContext.ProductUrls.AddRange(url1, url2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_RemovingMinPricedUrl_UpdatesProductCurrencyToNewMin()
    {
        // Arrange — product price/currency currently come from the EUR url.
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 30m;
        url1.Currency = "EUR";
        url2.CurrentPrice = 50m;
        url2.Currency = "USD";
        product.CurrentPrice = 30m;
        product.Currency = "EUR";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — remove the EUR url; the USD url is now the only contributor.
        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert — currency must follow the new min, or the UI renders a USD 50 listing as "€50".
        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.CurrentPrice.Should().Be(50m);
        updated.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_RemovingUrlThatChangesPrice_CapturesPreviousPrice()
    {
        // Arrange
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 30m;
        url2.CurrentPrice = 50m;
        product.CurrentPrice = 30m;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert — the dashboard's "% change" reads PreviousPrice; skipping it leaves a stale baseline.
        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.CurrentPrice.Should().Be(50m);
        updated.PreviousPrice.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_WithPausedRemainingUrl_ExcludesItFromProductPrice()
    {
        // Arrange — the surviving url is paused, so its price is frozen and must not define the MIN.
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 50m;
        url2.CurrentPrice = 30m;
        url2.Status = ProductUrlStatus.Paused;
        product.CurrentPrice = 30m;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert — no live url has a price left.
        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RemovingTheOnlyAnomalousUrl_ClearsProductAnomalyFlag()
    {
        // The product flag is derived from per-URL state; removing the URL that carried the anomaly
        // must recompute it, or it stays stuck until the next successful scrape.
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 50m;
        url1.HasPriceAnomaly = true;
        url2.CurrentPrice = 80m;
        product.CurrentPrice = 50m;
        product.HasPriceAnomaly = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RemovingCleanUrl_KeepsAnomalyFlagFromSurvivingUrl()
    {
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 50m;
        url2.CurrentPrice = 80m;
        url2.HasPriceAnomaly = true;
        product.CurrentPrice = 50m;
        product.HasPriceAnomaly = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.HasPriceAnomaly.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNoRemainingPricedUrl_ClearsProductPrice()
    {
        // Arrange
        var (product, url1, url2) = CreateProductWithTwoUrls();
        url1.CurrentPrice = 50m;
        url2.CurrentPrice = null;
        product.CurrentPrice = 50m;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _handler.Handle(new RemoveProductUrl.Command(product.Id, url1.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert
        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.CurrentPrice.Should().BeNull();
    }

    private (Product product, ProductUrl url1, ProductUrl url2) CreateProductWithTwoUrls()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);

        var url1 = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://amazon.com/product",
            Currency = "USD"
        };
        var url2 = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://walmart.com/product",
            Currency = "USD"
        };
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.SaveChanges();

        return (product, url1, url2);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
