using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Persistence;

public class CascadeDeleteTests : IDisposable
{
    private readonly OphiDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public CascadeDeleteTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
    }

    [Fact]
    public async Task Delete_Product_CascadesAlerts()
    {
        // Arrange
        var user = TestEntityFactory.User().Build();
        var product = TestEntityFactory.Product(user.Id).Build();
        var alert = TestEntityFactory.Alert(product.Id, user.Id).WithTarget(50m).Build();

        _dbContext.Users.Add(user);
        _dbContext.Products.Add(product);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var alertCount = await _dbContext.Alerts.CountAsync(TestContext.Current.CancellationToken);
        alertCount.Should().Be(0, "Deleting product should cascade delete alerts");
    }

    [Fact]
    public async Task Delete_Product_CascadesPricePoints()
    {
        // Arrange
        var user = TestEntityFactory.User().Build();
        var product = TestEntityFactory.Product(user.Id).Build();
        var pricePoint = new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 99.99m, Currency = "USD" };

        _dbContext.Users.Add(user);
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.Add(pricePoint);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var pricePointCount = await _dbContext.PricePoints.CountAsync(TestContext.Current.CancellationToken);
        pricePointCount.Should().Be(0, "Deleting product should cascade delete price points");
    }

    [Fact]
    public async Task Delete_Product_CascadesProductUrls()
    {
        // Arrange
        var user = TestEntityFactory.User().Build();
        var product = TestEntityFactory.Product(user.Id).Build();
        var productUrl = TestEntityFactory.ProductUrl(product.Id).WithUrl("https://example.com/product").Build();

        _dbContext.Users.Add(user);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var urlCount = await _dbContext.ProductUrls.CountAsync(TestContext.Current.CancellationToken);
        urlCount.Should().Be(0, "Deleting product should cascade delete product URLs");
    }

    [Fact]
    public async Task Delete_Product_CascadesScrapeLogs()
    {
        // Arrange
        var user = TestEntityFactory.User().Build();
        var product = TestEntityFactory.Product(user.Id).Build();
        var productUrl = TestEntityFactory.ProductUrl(product.Id).WithUrl("https://example.com/product").Build();
        var scrapeLog = new ScrapeLog { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = productUrl.Id, Success = true, Price = 99.99m };

        _dbContext.Users.Add(user);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.ScrapeLogs.Add(scrapeLog);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var logCount = await _dbContext.ScrapeLogs.CountAsync(TestContext.Current.CancellationToken);
        logCount.Should().Be(0, "Deleting product should cascade delete scrape logs");
    }

    [Fact]
    public async Task Delete_Product_SetsNullOnLinkedNotifications()
    {
        // Arrange
        var user = TestEntityFactory.User().Build();
        var product = TestEntityFactory.Product(user.Id).Build();
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProductId = product.Id,
            Title = "Test Alert",
            Message = "Price dropped",
            Type = NotificationType.PriceAlert
        };

        _dbContext.Users.Add(user);
        _dbContext.Products.Add(product);
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert — notification should still exist but with ProductId = null
        var existingNotification = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == notification.Id, TestContext.Current.CancellationToken);
        existingNotification.Should().NotBeNull("Notification should not be deleted when product is deleted");
        existingNotification!.ProductId.Should().BeNull("Notification.ProductId should be set to null (SetNull behavior)");
    }

    [Fact]
    public async Task Delete_User_CascadesAllProductsAndChildren()
    {
        // Arrange — two products, one with a URL + alert, one with just a URL
        var user = TestEntityFactory.User().Build();

        var product1 = TestEntityFactory.Product(user.Id).Named("Product 1").Build();
        var url1 = TestEntityFactory.ProductUrl(product1.Id).WithUrl("https://example.com/1").Build();
        var alert1 = TestEntityFactory.Alert(product1.Id, user.Id).WithTarget(50m).Build();

        var product2 = TestEntityFactory.Product(user.Id).Named("Product 2").Build();
        var url2 = TestEntityFactory.ProductUrl(product2.Id).WithUrl("https://example.com/2").Build();

        _dbContext.Users.Add(user);
        _dbContext.Products.AddRange(product1, product2);
        _dbContext.ProductUrls.AddRange(url1, url2);
        _dbContext.Alerts.Add(alert1);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var productCount = await _dbContext.Products.CountAsync(TestContext.Current.CancellationToken);
        var urlCount = await _dbContext.ProductUrls.CountAsync(TestContext.Current.CancellationToken);
        var alertCount = await _dbContext.Alerts.CountAsync(TestContext.Current.CancellationToken);

        productCount.Should().Be(0, "Deleting user should cascade delete products");
        urlCount.Should().Be(0, "Deleting user should cascade delete product URLs");
        alertCount.Should().Be(0, "Deleting user should cascade delete alerts");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
