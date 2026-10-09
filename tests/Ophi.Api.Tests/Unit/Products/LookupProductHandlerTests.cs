using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Products;

public class LookupProductHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly LookupProduct.Handler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public LookupProductHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new LookupProduct.Handler(_dbContext);
        _dbContext.Users.Add(new User { Id = _userId, Email = "lookup@example.com", Name = "Lookup", PasswordHash = "hash" });
        _dbContext.SaveChanges();
    }

    private (Product Product, ProductUrl Url) Track(Guid userId, string url)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Mug",
            CurrentPrice = 12.50m,
            Currency = "EUR",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = url, Currency = "EUR" };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.SaveChanges();
        return (product, productUrl);
    }

    [Fact]
    public async Task Handle_WithUrlCarryingTrackingParams_ReturnsTheTrackedProduct()
    {
        var (product, url) = Track(_userId, "https://shop.example/p/mug");

        var result = await _handler.Handle(
            new LookupProduct.Query(_userId, "https://www.shop.example/p/mug?utm_source=x&fbclid=y#reviews"),
            TestContext.Current.CancellationToken);

        result.ProductId.Should().Be(product.Id);
        result.ProductUrlId.Should().Be(url.Id);
        result.Name.Should().Be("Mug");
        result.Url.Should().Be("https://shop.example/p/mug");
        result.CurrentPrice.Should().Be(12.50m);
        result.Currency.Should().Be("EUR");
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_WithUntrackedUrl_ThrowsNotFound()
    {
        Track(_userId, "https://shop.example/p/mug");

        var act = () => _handler.Handle(
            new LookupProduct.Query(_userId, "https://shop.example/p/plate"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithUrlTrackedOnlyByAnotherUser_ThrowsNotFound()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User { Id = otherUserId, Email = "other@example.com", Name = "Other", PasswordHash = "hash" });
        _dbContext.SaveChanges();
        Track(otherUserId, "https://shop.example/p/mug");

        var act = () => _handler.Handle(
            new LookupProduct.Query(_userId, "https://shop.example/p/mug"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://shop.example/p/mug")]
    public void Validator_WithInvalidUrl_Fails(string url)
    {
        new LookupProduct.Validator().Validate(new LookupProduct.Query(_userId, url)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_WithUrlOverTheLimit_Fails()
    {
        var url = "https://shop.example/" + new string('a', 2048);

        new LookupProduct.Validator().Validate(new LookupProduct.Query(_userId, url)).IsValid.Should().BeFalse();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
