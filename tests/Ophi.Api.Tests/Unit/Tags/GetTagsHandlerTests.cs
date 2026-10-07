using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Tags;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Tags;

public class GetTagsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetTags.Handler _handler;
    private readonly Guid _testUserId;

    public GetTagsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetTags.Handler(_dbContext, NullLogger<GetTags.Handler>.Instance);
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
    public async Task Handle_WithNoTags_ReturnsEmptyList()
    {
        // Arrange
        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithTags_ReturnsUserTags()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(tag.Id);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectTagData()
    {
        // Arrange
        var tag = CreateTag("Electronics", "#FF0000", 5);
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var tagDto = result.Items[0];
        tagDto.Name.Should().Be("Electronics");
        tagDto.Color.Should().Be("#FF0000");
        tagDto.Weight.Should().Be(5);
        tagDto.ProductCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OnlyReturnsTagsForRequestedUser()
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

        var myTag = CreateTag("My Tag");

        var otherTag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Tag",
            Color = "#3B82F6",
            Weight = 0
        };

        _dbContext.Tags.AddRange(myTag, otherTag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(myTag.Id);
    }

    [Fact]
    public async Task Handle_ReturnsTagsOrderedByWeightDescendingThenName()
    {
        // Arrange
        var tagA = CreateTag("Apple", "#3B82F6", 5);
        var tagB = CreateTag("Banana", "#3B82F6", 10);
        var tagC = CreateTag("Cherry", "#3B82F6", 5);

        _dbContext.Tags.AddRange(tagA, tagB, tagC);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items[0].Name.Should().Be("Banana"); // Weight 10 (highest)
        result.Items[1].Name.Should().Be("Apple");  // Weight 5, alphabetically first
        result.Items[2].Name.Should().Be("Cherry"); // Weight 5, alphabetically second
    }

    [Fact]
    public async Task Handle_WithTagContainingProducts_ReturnsCorrectProductCount()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add products with tags
        var products = new[]
        {
            CreateProduct("Product 1"),
            CreateProduct("Product 2"),
            CreateProduct("Product 3")
        };
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add product tags
        var productTags = products.Select(p => new ProductTag { ProductId = p.Id, TagId = tag.Id }).ToList();
        _dbContext.ProductTags.AddRange(productTags);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].ProductCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithMultipleTags_ReturnsCorrectProductCountsForEach()
    {
        // Arrange
        var tag1 = CreateTag("Tag 1");
        var tag2 = CreateTag("Tag 2");
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add products
        var products = new[]
        {
            CreateProduct("Product 1A"),
            CreateProduct("Product 1B"),
            CreateProduct("Product 2A")
        };
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add product tags
        _dbContext.ProductTags.AddRange(
            new ProductTag { ProductId = products[0].Id, TagId = tag1.Id },
            new ProductTag { ProductId = products[1].Id, TagId = tag1.Id },
            new ProductTag { ProductId = products[2].Id, TagId = tag2.Id }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.First(t => t.Name == "Tag 1").ProductCount.Should().Be(2);
        result.Items.First(t => t.Name == "Tag 2").ProductCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithEmptyTag_ReturnsZeroProductCount()
    {
        // Arrange
        var tag = CreateTag("Empty Tag");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].ProductCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithSearch_ReturnsMatchingTags()
    {
        // Arrange
        var tag1 = CreateTag("Electronics");
        var tag2 = CreateTag("Clothing");
        var tag3 = CreateTag("Electronic Accessories");
        _dbContext.Tags.AddRange(tag1, tag2, tag3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId, "Electro");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().Contain(t => t.Name == "Electronics");
        result.Items.Should().Contain(t => t.Name == "Electronic Accessories");
    }

    [Fact]
    public async Task Handle_WithSearch_NoMatch_ReturnsEmpty()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId, "Xyz");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithSearchContainingPercent_MatchesOnlyLiteralPercent()
    {
        // Arrange — "%" is a LIKE wildcard; without escaping, "50%" matches any name containing "50".
        var literal = CreateTag("50% off");
        var decoy = CreateTag("500 deals");
        _dbContext.Tags.AddRange(literal, decoy);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId, "50%");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("50% off");
    }

    [Fact]
    public async Task Handle_WithNullSearch_ReturnsAllTags()
    {
        // Arrange
        var tag1 = CreateTag("Electronics");
        var tag2 = CreateTag("Clothing");
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetTags.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
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
