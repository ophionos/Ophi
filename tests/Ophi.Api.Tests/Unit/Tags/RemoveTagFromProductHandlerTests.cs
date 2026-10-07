using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Tags;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Tags;

public class RemoveTagFromProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly RemoveTagFromProduct.Handler _handler;
    private readonly Guid _testUserId;

    public RemoveTagFromProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new RemoveTagFromProduct.Handler(_dbContext, NullLogger<RemoveTagFromProduct.Handler>.Instance);
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
    public async Task Handle_WithValidData_RemovesTagFromProduct()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        var tag = CreateTag("Electronics");
        _dbContext.Products.Add(product);
        _dbContext.Tags.Add(tag);
        _dbContext.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, tag.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var productTag = await _dbContext.ProductTags
            .FirstOrDefaultAsync(pt => pt.ProductId == product.Id && pt.TagId == tag.Id, TestContext.Current.CancellationToken);
        productTag.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new RemoveTagFromProduct.Command(Guid.NewGuid(), Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

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

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Tag",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Products.Add(product);
        _dbContext.Tags.Add(tag);
        _dbContext.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, tag.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithTagNotOnProduct_ThrowsNotFoundException()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        var tag = CreateTag("Electronics");
        _dbContext.Products.Add(product);
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, tag.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found on product");
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherTags()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        var tag1 = CreateTag("Electronics");
        var tag2 = CreateTag("Sale");
        _dbContext.Products.Add(product);
        _dbContext.Tags.AddRange(tag1, tag2);
        _dbContext.ProductTags.AddRange(
            new ProductTag { ProductId = product.Id, TagId = tag1.Id },
            new ProductTag { ProductId = product.Id, TagId = tag2.Id }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, tag1.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var remainingTags = await _dbContext.ProductTags
            .Where(pt => pt.ProductId == product.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        remainingTags.Should().HaveCount(1);
        remainingTags[0].TagId.Should().Be(tag2.Id);
    }

    [Fact]
    public async Task Handle_WithOtherUsersTag_ThrowsNotFoundException()
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

        var product = CreateProduct("Test Product");
        var otherTag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Tag",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Products.Add(product);
        _dbContext.Tags.Add(otherTag);
        _dbContext.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = otherTag.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, otherTag.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found");
    }

    [Fact]
    public async Task Handle_DoesNotDeleteTag()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        var tag = CreateTag("Electronics");
        _dbContext.Products.Add(product);
        _dbContext.Tags.Add(tag);
        _dbContext.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveTagFromProduct.Command(product.Id, tag.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var remainingTag = await _dbContext.Tags.FindAsync([tag.Id], TestContext.Current.CancellationToken);
        remainingTag.Should().NotBeNull();
    }

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    private Tag CreateTag(string name)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Color = "#3B82F6",
            Weight = 0
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
