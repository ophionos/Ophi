using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Features.Products;

public class ResumeProductUrlTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId;
    private readonly ResumeProductUrl.Handler _handler;

    public ResumeProductUrlTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new OphiDbContext(options);
        _dbContext.Database.EnsureCreated();

        _testUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();

        _handler = new ResumeProductUrl.Handler(_dbContext, NullLogger<ResumeProductUrl.Handler>.Instance);
    }

    [Theory]
    [InlineData(ProductStatus.Error, ProductStatus.Active)]
    [InlineData(ProductStatus.Paused, ProductStatus.Paused)]
    public async Task Handle_OnProductInStatus_LeavesProductSchedulable(ProductStatus before, ProductStatus expected)
    {
        // The dispatcher scans only Active products, so a URL resumed on an errored product was
        // never scraped again. A product the user paused stays paused: resuming a URL is not
        // resuming the product.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Errored Product",
            Currency = "USD",
            Status = before
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/errored",
            Currency = "USD",
            Status = ProductUrlStatus.Paused,
            FailureCount = 5
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _handler.Handle(new ResumeProductUrl.Command(product.Id, productUrl.Id, _testUserId), TestContext.Current.CancellationToken);

        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_PausedUrl_ResumesSuccessfully()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            Status = ProductUrlStatus.Paused,
            SuspiciousCount = 3,
            SuspiciousReason = "Redirected to different domain",
            FailureCount = 2
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ResumeProductUrl.Command(product.Id, productUrl.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Active);
        updatedUrl.SuspiciousCount.Should().Be(0);
        updatedUrl.SuspiciousReason.Should().BeNull();
        updatedUrl.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WrongUser_ThrowsNotFoundException()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            Status = ProductUrlStatus.Paused
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ResumeProductUrl.Command(product.Id, productUrl.Id, Guid.NewGuid());

        // Act & Assert
        await _handler.Invoking(h => h.Handle(command, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new ResumeProductUrl.Command(Guid.NewGuid(), Guid.NewGuid(), _testUserId);

        // Act & Assert
        await _handler.Invoking(h => h.Handle(command, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NonExistentUrl_ThrowsNotFoundException()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ResumeProductUrl.Command(product.Id, Guid.NewGuid(), _testUserId);

        // Act & Assert
        await _handler.Invoking(h => h.Handle(command, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ResumingTheOnlyAnomalousUrl_ClearsProductAnomalyFlag()
    {
        // Resume() clears the URL's anomaly flag. The product flag is derived from per-URL state and
        // is otherwise only recomputed by the scrape handler, so without an explicit recompute here
        // the product would keep showing an anomaly for at least a full check interval.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            HasPriceAnomaly = true
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            Status = ProductUrlStatus.Paused,
            HasPriceAnomaly = true
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _handler.Handle(new ResumeProductUrl.Command(product.Id, productUrl.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.HasPriceAnomaly.Should().BeFalse();
        updatedUrl.Status.Should().Be(ProductUrlStatus.Active);

        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ResumingOneUrlWhileAnotherIsAnomalous_KeepsProductAnomalyFlag()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            HasPriceAnomaly = true
        };
        var resumedUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/resumed",
            Currency = "USD",
            Status = ProductUrlStatus.Paused,
            HasPriceAnomaly = true
        };
        var stillAnomalousUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/other",
            Currency = "USD",
            HasPriceAnomaly = true
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(resumedUrl, stillAnomalousUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _handler.Handle(new ResumeProductUrl.Command(product.Id, resumedUrl.Id, _testUserId), TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
