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

public class GetScrapeLogHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetScrapeLog.Handler _handler;
    private readonly Guid _testUserId;

    public GetScrapeLogHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetScrapeLog.Handler(_dbContext, NullLogger<GetScrapeLog.Handler>.Instance);
        _testUserId = Guid.NewGuid();

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

    private (Product product, ProductUrl productUrl) CreateProduct(string name = "Test Product")
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        return (product, productUrl);
    }

    [Fact]
    public async Task Handle_WithValidProduct_ReturnsScrapeLog()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.ScrapeLogs.Add(new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Success = true,
            Price = 99.99m,
            DurationMs = 150
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeLog.Query(product.Id, _testUserId, 20);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Success.Should().BeTrue();
        result.Items[0].Price.Should().Be(99.99m);
        result.Items[0].DurationMs.Should().Be(150);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFound()
    {
        // Arrange
        var query = new GetScrapeLog.Query(Guid.NewGuid(), _testUserId, 20);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_OrderedByCreatedAtDesc()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);

        var older = new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Success = true,
            Price = 10m,
            DurationMs = 100,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        var newer = new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Success = false,
            Error = "fail",
            DurationMs = 200,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _dbContext.ScrapeLogs.AddRange(older, newer);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeLog.Query(product.Id, _testUserId, 20);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Success.Should().BeFalse(); // newer first
        result.Items[1].Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LimitRespected()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);

        for (var i = 0; i < 5; i++)
        {
            _dbContext.ScrapeLogs.Add(new ScrapeLog
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductUrlId = productUrl.Id,
                Success = true,
                Price = 10m + i,
                DurationMs = 100
            });
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeLog.Query(product.Id, _testUserId, 3);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_WithOtherUsersProduct_ThrowsNotFound()
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
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeLog.Query(product.Id, _testUserId, 20);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_IncludesProductUrlInfo()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.ScrapeLogs.Add(new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Success = true,
            Price = 50m,
            DurationMs = 100
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeLog.Query(product.Id, _testUserId, 20);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Url.Should().Be("https://example.com/product");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
