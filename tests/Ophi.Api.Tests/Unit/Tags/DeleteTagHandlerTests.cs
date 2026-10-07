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

public class DeleteTagHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly DeleteTag.Handler _handler;
    private readonly Guid _testUserId;

    public DeleteTagHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new DeleteTag.Handler(_dbContext, NullLogger<DeleteTag.Handler>.Instance);
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
    public async Task Handle_WithExistingTag_DeletesTag()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteTag.Command(tag.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedTag = await _dbContext.Tags.FindAsync([tag.Id], TestContext.Current.CancellationToken);
        deletedTag.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentTag_ThrowsNotFoundException()
    {
        // Arrange
        var command = new DeleteTag.Command(Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found");
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

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Tag",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteTag.Command(tag.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found");
    }

    [Fact]
    public async Task Handle_WithTagHavingProducts_CascadeDeletesProductTags()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var product = CreateProduct("Product 1");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var productTag = new ProductTag { ProductId = product.Id, TagId = tag.Id };
        _dbContext.ProductTags.Add(productTag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteTag.Command(tag.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedTag = await _dbContext.Tags.FindAsync([tag.Id], TestContext.Current.CancellationToken);
        deletedTag.Should().BeNull();

        var deletedProductTag = await _dbContext.ProductTags
            .FirstOrDefaultAsync(pt => pt.TagId == tag.Id, TestContext.Current.CancellationToken);
        deletedProductTag.Should().BeNull();

        // Product should still exist
        var remainingProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        remainingProduct.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DoesNotDeleteOtherTags()
    {
        // Arrange
        var tag1 = CreateTag("Tag 1");
        var tag2 = CreateTag("Tag 2");
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteTag.Command(tag1.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var remainingTag = await _dbContext.Tags.FindAsync([tag2.Id], TestContext.Current.CancellationToken);
        remainingTag.Should().NotBeNull();
    }

    private Tag CreateTag(string name, string color = "#3B82F6", int weight = 0)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Color = color,
            Weight = weight
        };
    }

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

