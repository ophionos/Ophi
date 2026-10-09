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

public class AddProductUrlHandlerTests : IDisposable
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly AddProductUrl.Handler _handler;
    private readonly Guid _testUserId;

    public AddProductUrlHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _messageBusMock = new Mock<IMessageBus>();
        _handler = new AddProductUrl.Handler(_dbContext, _messageBusMock.Object, NullLogger<AddProductUrl.Handler>.Instance);
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
    public async Task Handle_WithValidUrl_CreatesProductUrl()
    {
        // Arrange
        var product = CreateProductWithUrl("Test Product", "https://amazon.com/product");
        var command = new AddProductUrl.Command(product.Id, "https://walmart.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be("https://walmart.com/product");
        result.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithValidUrl_PublishesScrapeCommand()
    {
        // Arrange
        var product = CreateProductWithUrl("Test Product", "https://amazon.com/product");
        var command = new AddProductUrl.Command(product.Id, "https://walmart.com/product") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        _messageBusMock.Verify(
            x => x.PublishAsync(
                It.Is<ScrapeProductUrlCommand>(cmd => cmd.ProductUrlId == result.Id),
                It.IsAny<DeliveryOptions?>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateUrl_ThrowsApiException()
    {
        // Arrange
        var product = CreateProductWithUrl("Test Product", "https://amazon.com/product");
        var command = new AddProductUrl.Command(product.Id, "https://amazon.com/product") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("This URL is already tracked");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new AddProductUrl.Command(Guid.NewGuid(), "https://walmart.com/product") { UserId = _testUserId };

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
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://amazon.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductUrl.Command(product.Id, "https://walmart.com/product") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SavesProductUrlToDatabase()
    {
        // Arrange
        var product = CreateProductWithUrl("Test Product", "https://amazon.com/product");
        var command = new AddProductUrl.Command(product.Id, "https://walmart.com/product") { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var urls = _dbContext.ProductUrls.Where(pu => pu.ProductId == product.Id).ToList();
        urls.Should().HaveCount(2);
        urls.Should().Contain(pu => pu.Url == "https://walmart.com/product");
    }

    [Fact]
    public async Task Handle_WithUrlTrackedOnAnotherProduct_ThrowsConflictNamingThatProduct()
    {
        // Arrange — the duplicate check spans all the user's products, so the 409 must name the
        // product that holds the URL, not the product in the route.
        var holder = CreateProductWithUrl("Holder", "https://amazon.com/dp/B000123");
        var target = CreateProductWithUrl("Target", "https://walmart.com/product");
        var holderUrlId = _dbContext.ProductUrls.Single(pu => pu.ProductId == holder.Id).Id;
        var command = new AddProductUrl.Command(target.Id, "https://www.amazon.com/dp/B000123/ref=sr_1_1?qid=9") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var thrown = await act.Should().ThrowAsync<ConflictException>();
        thrown.Which.Message.Should().Be("This URL is already tracked");
        thrown.Which.ProductId.Should().Be(holder.Id);
        thrown.Which.ProductUrlId.Should().Be(holderUrlId);
    }

    private Product CreateProductWithUrl(string name, string url)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = url,
            Currency = "USD"
        });
        _dbContext.SaveChanges();
        return product;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
