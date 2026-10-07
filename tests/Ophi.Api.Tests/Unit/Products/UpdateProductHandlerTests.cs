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

public class UpdateProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly UpdateProduct.Handler _handler;
    private readonly Guid _testUserId;

    public UpdateProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new UpdateProduct.Handler(_dbContext, NullLogger<UpdateProduct.Handler>.Instance);
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
    public async Task Handle_WithValidName_UpdatesName()
    {
        // Arrange
        var product = CreateProduct("Old Name", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, "New Name", null, null, null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("New Name");
        result.Id.Should().Be(product.Id);
    }

    [Fact]
    public async Task Handle_WithValidImageUrl_UpdatesImageUrl()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, "https://example.com/new-image.jpg", null, null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.ImageUrl.Should().Be("https://example.com/new-image.jpg");
    }

    [Fact]
    public async Task Handle_WithStatusActiveToPaused_UpdatesStatus()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, "paused", null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be("paused");
    }

    [Fact]
    public async Task Handle_WithStatusPausedToActive_UpdatesStatus()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Paused);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, "active", null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), "New Name", null, null, null)
        {
            UserId = _testUserId
        };

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

        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.UserId = otherUserId;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, "New Name", null, null, null)
        {
            UserId = _testUserId
        };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithInvalidStatusTransition_ThrowsApiException()
    {
        // Arrange - product in Pending state cannot be paused
        var product = CreateProduct("Test Product", ProductStatus.Pending);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, "paused", null)
        {
            UserId = _testUserId
        };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Cannot change status of a product in 'pending' state");
    }

    [Fact]
    public async Task Handle_WithErrorStatusTransition_ThrowsApiException()
    {
        // Arrange - product in Error state cannot be activated
        var product = CreateProduct("Test Product", ProductStatus.Error);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, "active", null)
        {
            UserId = _testUserId
        };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Cannot change status of a product in 'error' state");
    }

    [Fact]
    public async Task Handle_WithMultipleFields_UpdatesAll()
    {
        // Arrange
        var product = CreateProduct("Old Name", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(
            product.Id,
            "New Name",
            "https://example.com/new-image.jpg",
            "paused",
            null
        )
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("New Name");
        result.ImageUrl.Should().Be("https://example.com/new-image.jpg");
        result.Status.Should().Be("paused");
    }

    [Fact]
    public async Task Handle_WithNullFields_DoesNotChangeExistingValues()
    {
        // Arrange
        var product = CreateProduct("Original Name", ProductStatus.Active);
        product.ImageUrl = "https://example.com/original.jpg";
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Only update status, leave name and imageUrl null
        var command = new UpdateProduct.Command(product.Id, null, null, "paused", null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Original Name");
        result.ImageUrl.Should().Be("https://example.com/original.jpg");
        result.Status.Should().Be("paused");
    }

    [Fact]
    public async Task Handle_WithIsFavouriteTrue_SetsFavourite()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.IsFavourite = false;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, null, true)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsFavourite.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithIsFavouriteFalse_UnsetsFavourite()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.IsFavourite = true;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, null, false)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsFavourite.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithIsFavouriteNull_DoesNotChangeFavourite()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.IsFavourite = true;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, "New Name", null, null, null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsFavourite.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithCustomFields_SetsCustomFields()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var customFields = new List<CustomFieldDto>
        {
            new("Color", "Red"),
            new("Size", "Large")
        };
        var command = new UpdateProduct.Command(product.Id, null, null, null, null, customFields)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CustomFields.Should().HaveCount(2);
        result.CustomFields.Should().Contain(cf => cf.Name == "Color" && cf.Value == "Red");
        result.CustomFields.Should().Contain(cf => cf.Name == "Size" && cf.Value == "Large");
    }

    [Fact]
    public async Task Handle_WithCustomFieldsNull_DoesNotClearExistingFields()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.CustomFields = [new CustomField("Color", "Red")];
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, "New Name", null, null, null)
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CustomFields.Should().HaveCount(1);
        result.CustomFields.Should().Contain(cf => cf.Name == "Color" && cf.Value == "Red");
    }

    [Fact]
    public async Task Handle_WithEmptyCustomFieldsList_ClearsCustomFields()
    {
        // Arrange
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.CustomFields = [new CustomField("Color", "Red")];
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, null, null, [])
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CustomFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithCheckIntervalMinutes_UpdatesInterval()
    {
        var product = CreateProduct("Test Product", ProductStatus.Active);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, null, null, null, 120)
        {
            UserId = _testUserId
        };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.CheckIntervalMinutes.Should().Be(120);
        var updated = _dbContext.Products.First(p => p.Id == product.Id);
        updated.CheckIntervalMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Handle_WithCheckIntervalZero_ResetsToDefault()
    {
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.CheckIntervalMinutes = 120;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, null, null, null, null, null, 0)
        {
            UserId = _testUserId
        };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.CheckIntervalMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNullCheckInterval_DoesNotChangeInterval()
    {
        var product = CreateProduct("Test Product", ProductStatus.Active);
        product.CheckIntervalMinutes = 120;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateProduct.Command(product.Id, "New Name", null, null, null)
        {
            UserId = _testUserId
        };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.CheckIntervalMinutes.Should().Be(120);
    }

    private Product CreateProduct(string name, ProductStatus status)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = status
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
        GC.SuppressFinalize(this);
    }
}
