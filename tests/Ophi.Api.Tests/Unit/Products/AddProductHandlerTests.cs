using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

public class AddProductHandlerTests : IDisposable
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly AddProduct.Handler _handler;
    private readonly Guid _testUserId;

    public AddProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _messageBusMock = new Mock<IMessageBus>();
        _handler = new AddProduct.Handler(_dbContext, _messageBusMock.Object, NullLogger<AddProduct.Handler>.Instance);
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
    public async Task Handle_WithValidUrl_ReturnsProductWithPendingStatus()
    {
        // Arrange
        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be(command.Url);
        result.Name.Should().Be("Loading...");
        result.Status.Should().Be("pending");
        result.CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithValidUrl_SavesProductWithPendingStatusToDatabase()
    {
        // Arrange
        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedProduct = await _dbContext.Products.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedProduct.Should().NotBeNull();
        savedProduct.UserId.Should().Be(_testUserId);
        savedProduct.Name.Should().Be("Loading...");
        savedProduct.Status.Should().Be(ProductStatus.Pending);
        savedProduct.CurrentPrice.Should().BeNull();

        var savedUrl = await _dbContext.ProductUrls.FirstOrDefaultAsync(pu => pu.ProductId == result.Id, TestContext.Current.CancellationToken);
        savedUrl.Should().NotBeNull();
        savedUrl.Url.Should().Be(command.Url);
    }

    [Fact]
    public async Task Handle_WithValidUrl_PublishesScrapeCommand()
    {
        // Arrange
        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var productUrl = await _dbContext.ProductUrls.FirstAsync(pu => pu.ProductId == result.Id, TestContext.Current.CancellationToken);
        _messageBusMock.Verify(
            x => x.PublishAsync(
                It.Is<ScrapeProductUrlCommand>(cmd => cmd.ProductUrlId == productUrl.Id),
                It.IsAny<DeliveryOptions?>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidUrl_DoesNotCreatePricePoint()
    {
        // Arrange
        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var pricePoints = await _dbContext.PricePoints
            .Where(pp => pp.ProductId == result.Id)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        pricePoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithDuplicateUrl_ThrowsApiException()
    {
        // Arrange
        var existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Existing Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(existingProduct);
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("You are already tracking this product");
    }

    [Fact]
    public async Task Handle_WithSameUrlDifferentUser_AllowsProductCreation()
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

        var existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Existing Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(existingProduct);
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        var products = await _dbContext.Products.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        products.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithDuplicateUrlPendingStatus_ThrowsApiException()
    {
        // Arrange - existing product with pending status
        var existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        _dbContext.Products.Add(existingProduct);
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProduct.Command("https://example.com/product") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert - should still throw duplicate error even for pending products
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("You are already tracking this product");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
