using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Products;

public class CreateProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly CreateProduct.Handler _handler;
    private readonly Guid _testUserId;

    public CreateProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new CreateProduct.Handler(_dbContext, NullLogger<CreateProduct.Handler>.Instance);
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
    public async Task Handle_WithNameOnly_CreatesActiveProduct()
    {
        // Arrange
        var command = new CreateProduct.Command("My Product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("My Product");
        result.Status.Should().Be("active");
        result.CurrentPrice.Should().BeNull();
        result.ImageUrl.Should().BeNull();
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_WithAllFields_CreatesProductWithProvidedValues()
    {
        // Arrange
        var command = new CreateProduct.Command("My Product", "https://example.com/image.jpg", "EUR")
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("My Product");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
        result.Currency.Should().Be("EUR");
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_SavesProductToDatabase()
    {
        // Arrange
        var command = new CreateProduct.Command("My Product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedProduct = await _dbContext.Products.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedProduct.Should().NotBeNull();
        savedProduct.UserId.Should().Be(_testUserId);
        savedProduct.Name.Should().Be("My Product");
        savedProduct.Status.Should().Be(ProductStatus.Active);
        savedProduct.CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DoesNotCreateProductUrls()
    {
        // Arrange
        var command = new CreateProduct.Command("My Product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var urls = await _dbContext.ProductUrls
            .Where(pu => pu.ProductId == result.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        urls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNullCurrency_DefaultsToUSD()
    {
        // Arrange
        var command = new CreateProduct.Command("My Product", Currency: null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Currency.Should().Be("USD");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
