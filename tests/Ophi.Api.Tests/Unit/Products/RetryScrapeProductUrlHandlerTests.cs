using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Tests.Unit.Products;

public class RetryScrapeProductUrlHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly RetryScrapeProductUrl.Handler _handler;
    private readonly Guid _testUserId;

    public RetryScrapeProductUrlHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _messageBusMock = new Mock<IMessageBus>();
        _handler = new RetryScrapeProductUrl.Handler(_dbContext, _messageBusMock.Object, NullLogger<RetryScrapeProductUrl.Handler>.Instance);
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
    public async Task Handle_WithValidProductUrl_MarksUrlDueAndClearsFailureState()
    {
        // Arrange — a previously-checked URL with a stale failure
        var (product, productUrl) = CreateProductWithUrl("Test Product", "https://amazon.com/product",
            lastCheckedAt: DateTime.UtcNow.AddMinutes(-5), failureCount: 2, lastError: "boom");

        var command = new RetryScrapeProductUrl.Command(product.Id, productUrl.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — the URL is reset so both the instant scrape and the dispatcher backstop apply
        var updated = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updated!.LastCheckedAt.Should().BeNull();
        updated.Status.Should().Be(ProductUrlStatus.Active);
        updated.FailureCount.Should().Be(0);
        updated.LastError.Should().BeNull();

        // Assert — a forced scrape is published as the instant trigger (durable transport / in-process)
        _messageBusMock.Verify(
            x => x.PublishAsync(
                It.Is<ScrapeProductUrlCommand>(cmd => cmd.ProductUrlId == productUrl.Id && cmd.Force),
                It.IsAny<DeliveryOptions?>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithErroredProductAndPausedUrl_ReactivatesBothSoDispatcherPicksItUp()
    {
        // Arrange — an errored product whose URL was auto-paused after repeated failures
        var (product, productUrl) = CreateProductWithUrl("Test Product", "https://amazon.com/product",
            productStatus: ProductStatus.Error, urlStatus: ProductUrlStatus.Paused,
            lastCheckedAt: DateTime.UtcNow.AddMinutes(-5));

        var command = new RetryScrapeProductUrl.Command(product.Id, productUrl.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — the dispatcher only scans Active products and non-paused URLs
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Active);
        updatedUrl.LastCheckedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new RetryScrapeProductUrl.Command(Guid.NewGuid(), Guid.NewGuid(), _testUserId);

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

        var (product, productUrl) = CreateProductWithUrl("Other Product", "https://amazon.com/product", otherUserId);
        var command = new RetryScrapeProductUrl.Command(product.Id, productUrl.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithUrlNotBelongingToProduct_ThrowsNotFoundException()
    {
        // Arrange
        var (product1, _) = CreateProductWithUrl("Product 1", "https://amazon.com/product1");
        var (_, productUrl2) = CreateProductWithUrl("Product 2", "https://walmart.com/product2");
        var command = new RetryScrapeProductUrl.Command(product1.Id, productUrl2.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product URL not found");
    }

    private (Product product, ProductUrl productUrl) CreateProductWithUrl(
        string name, string url, Guid? userId = null,
        ProductStatus productStatus = ProductStatus.Active,
        ProductUrlStatus urlStatus = ProductUrlStatus.Active,
        DateTime? lastCheckedAt = null, int failureCount = 0, string? lastError = null)
    {
        var ownerId = userId ?? _testUserId;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Name = name,
            Currency = "USD",
            Status = productStatus
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = url,
            Currency = "USD",
            Status = urlStatus,
            LastCheckedAt = lastCheckedAt,
            FailureCount = failureCount,
            LastError = lastError
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.SaveChanges();
        return (product, productUrl);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
