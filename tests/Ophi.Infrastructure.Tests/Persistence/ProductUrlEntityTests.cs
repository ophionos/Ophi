using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Persistence;

public class ProductUrlEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId;

    public ProductUrlEntityTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
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
    public async Task ProductUrl_CanBePersisted()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            SelectorType = SelectorType.Auto
        };
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var saved = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);

        // Assert
        saved.Should().NotBeNull();
        saved.Url.Should().Be("https://example.com/product");
        saved.ProductId.Should().Be(product.Id);
        saved.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ProductUrl_UniqueIndex_PreventsDirectDuplicateUrlOnSameProduct()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });

        var act = async () => await _dbContext.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProductUrl_AllowsSameUrlOnDifferentProducts()
    {
        // Arrange
        var product1 = CreateProduct("Product 1");
        var product2 = CreateProduct("Product 2");
        _dbContext.Products.AddRange(product1, product2);

        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product1.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product2.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var urls = await _dbContext.ProductUrls.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        urls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProductUrl_CascadeDeletesWhenProductIsDeleted()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var urls = await _dbContext.ProductUrls.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        urls.Should().BeEmpty();
    }

    [Fact]
    public async Task PricePoint_ProductUrlId_SetToNullWhenProductUrlDeleted()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.ProductUrls.Add(productUrl);

        var pricePoint = new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Price = 99.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow
        };
        _dbContext.PricePoints.Add(pricePoint);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.ProductUrls.Remove(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var savedPricePoint = await _dbContext.PricePoints.FindAsync([pricePoint.Id], TestContext.Current.CancellationToken);
        savedPricePoint.Should().NotBeNull();
        savedPricePoint.ProductUrlId.Should().BeNull();
    }

    [Fact]
    public async Task ProductUrl_NavigationToProduct_Works()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var loadedUrl = await _dbContext.ProductUrls
            .Include(pu => pu.Product)
            .FirstAsync(pu => pu.Id == productUrl.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        loadedUrl.Product.Should().NotBeNull();
        loadedUrl.Product.Name.Should().Be("Test Product");
    }

    [Fact]
    public async Task Product_ProductUrls_NavigationWorks()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://amazon.com/product",
            Currency = "USD"
        });
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://walmart.com/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var loaded = await _dbContext.Products
            .Include(p => p.ProductUrls)
            .FirstAsync(p => p.Id == product.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        loaded.ProductUrls.Should().HaveCount(2);
    }

    private Product CreateProduct(string name)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
