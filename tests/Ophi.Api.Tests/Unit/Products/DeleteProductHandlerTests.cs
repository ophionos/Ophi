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

public class DeleteProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly DeleteProduct.Handler _handler;
    private readonly Guid _testUserId;

    public DeleteProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new DeleteProduct.Handler(_dbContext, NullLogger<DeleteProduct.Handler>.Instance);
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
    public async Task Handle_WithValidProduct_DeletesProduct()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(product.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        deletedProduct.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithValidProduct_RemovesProductFromDatabase()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var initialCount = await _dbContext.Products.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var command = new DeleteProduct.Command(product.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var finalCount = await _dbContext.Products.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        finalCount.Should().Be(initialCount - 1);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var nonExistentProductId = Guid.NewGuid();
        var command = new DeleteProduct.Command(nonExistentProductId, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

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

        var otherUsersProduct = CreateProduct("Other's Product", "https://example.com/other");
        otherUsersProduct.UserId = otherUserId;
        _dbContext.Products.Add(otherUsersProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(otherUsersProduct.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithProductBelongingToDifferentUser_DoesNotDeleteProduct()
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

        var otherUsersProduct = CreateProduct("Other's Product", "https://example.com/other");
        otherUsersProduct.UserId = otherUserId;
        _dbContext.Products.Add(otherUsersProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(otherUsersProduct.Id, _testUserId);

        // Act
        try
        {
            await _handler.Handle(command, TestContext.Current.CancellationToken);
        }
        catch (NotFoundException)
        {
            // Expected
        }

        // Assert - product should still exist
        var product = await _dbContext.Products.FindAsync([otherUsersProduct.Id], TestContext.Current.CancellationToken);
        product.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherProducts()
    {
        // Arrange
        var productToDelete = CreateProduct("Delete Me", "https://example.com/delete");
        var productToKeep = CreateProduct("Keep Me", "https://example.com/keep");

        _dbContext.Products.AddRange(productToDelete, productToKeep);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(productToDelete.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var remainingProducts = await _dbContext.Products.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        remainingProducts.Should().HaveCount(1);
        remainingProducts[0].Id.Should().Be(productToKeep.Id);
    }

    [Fact]
    public async Task Handle_WithProductHavingPricePoints_DeletesProduct()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add some price points
        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 90m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(product.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        deletedProduct.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithProductHavingAlerts_DeletesProduct()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add an alert
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = _testUserId,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProduct.Command(product.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        deletedProduct.Should().BeNull();
    }

    private Product CreateProduct(string name, string url)
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
            Url = url,
            Currency = "USD"
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
