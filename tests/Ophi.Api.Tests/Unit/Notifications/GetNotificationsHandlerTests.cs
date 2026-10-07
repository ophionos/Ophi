using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common;
using Ophi.Api.Features.Notifications;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Notifications;

public class GetNotificationsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetNotifications.Handler _handler;
    private readonly Guid _testUserId;

    public GetNotificationsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetNotifications.Handler(_dbContext, NullLogger<GetNotifications.Handler>.Instance);
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

    [Fact]
    public async Task Handle_WithNoNotifications_ReturnsEmptyList()
    {
        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsOnlyUserNotifications()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other",
            PasswordHash = "hash"
        });

        _dbContext.Notifications.AddRange(
            CreateNotification("My Alert"),
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                Title = "Other's Alert",
                Message = "msg",
                Type = NotificationType.PriceAlert
            });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Be("My Alert");
    }

    [Fact]
    public async Task Handle_ReturnsOrderedByCreatedAtDescending()
    {
        var n1 = CreateNotification("First");
        var n2 = CreateNotification("Second");
        var n3 = CreateNotification("Third");
        _dbContext.Notifications.AddRange(n1, n2, n3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(3);
        // All created at same time via SaveChanges, but ordering should work
        result.Items.Select(n => n.Id).Should().ContainInOrder(n3.Id, n2.Id, n1.Id);
    }

    [Fact]
    public async Task Handle_WithUnreadOnlyTrue_FiltersReadNotifications()
    {
        var unread = CreateNotification("Unread");
        var read = CreateNotification("Read");
        read.IsRead = true;
        _dbContext.Notifications.AddRange(unread, read);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, true);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Be("Unread");
    }

    [Fact]
    public async Task Handle_MapsTypeCorrectly()
    {
        _dbContext.Notifications.Add(CreateNotification("Alert"));
        _dbContext.Notifications.Add(CreateNotification("Error", NotificationType.ScrapeError));
        _dbContext.Notifications.Add(CreateNotification("System", NotificationType.System));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Select(n => n.Type).Should().BeEquivalentTo(["priceAlert", "scrapeError", "system"]);
    }

    [Fact]
    public async Task Handle_IncludesProductName()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);

        CreateNotification("Price dropped");
        // Need to create a new notification with ProductId set (init property)
        var notifWithProduct = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Title = "Price dropped",
            Message = "msg",
            Type = NotificationType.PriceAlert,
            ProductId = product.Id
        };
        _dbContext.Notifications.Add(notifWithProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items[0].ProductId.Should().Be(product.Id);
        result.Items[0].ProductName.Should().Be("Test Product");
    }

    [Fact]
    public async Task Handle_WithoutProduct_ProductNameIsNull()
    {
        _dbContext.Notifications.Add(CreateNotification("System notification", NotificationType.System));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items[0].ProductId.Should().BeNull();
        result.Items[0].ProductName.Should().BeNull();
    }

    // Pagination tests
    [Fact]
    public async Task Handle_WithDefaultPagination_ReturnsFirstPage()
    {
        for (var i = 0; i < 30; i++)
        {
            _dbContext.Notifications.Add(CreateNotification($"Notification {i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(PaginationDefaults.DefaultPageSize);
        result.Total.Should().Be(30);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(PaginationDefaults.DefaultPageSize);
    }

    [Fact]
    public async Task Handle_WithCustomPageAndPageSize_ReturnsCorrectPage()
    {
        for (var i = 0; i < 10; i++)
        {
            _dbContext.Notifications.Add(CreateNotification($"Notification {i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false, Page: 2, PageSize: 3);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(3);
        result.Total.Should().Be(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithPageBeyondResults_ReturnsEmptyItemsWithCorrectTotal()
    {
        for (var i = 0; i < 5; i++)
        {
            _dbContext.Notifications.Add(CreateNotification($"Notification {i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false, Page: 100, PageSize: 10);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(5);
        result.Page.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithPageSizeExceeding100_CapsAt100()
    {
        _dbContext.Notifications.Add(CreateNotification("Notification 1"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false, PageSize: 200);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithUnreadOnlyAndPagination_TotalReflectsFilteredCount()
    {
        for (var i = 0; i < 5; i++)
        {
            _dbContext.Notifications.Add(CreateNotification($"Unread {i}"));
        }
        for (var i = 0; i < 3; i++)
        {
            var read = CreateNotification($"Read {i}");
            read.IsRead = true;
            _dbContext.Notifications.Add(read);
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, true, Page: 1, PageSize: 2);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Handle_WithPageBelowOne_ClampsToPageOne(int page)
    {
        _dbContext.Notifications.Add(CreateNotification("Test"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false, Page: page);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Handle_WithPageSizeBelowOne_ClampsToOne(int pageSize)
    {
        for (var i = 0; i < 3; i++)
        {
            _dbContext.Notifications.Add(CreateNotification($"Notification {i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotifications.Query(_testUserId, false, PageSize: pageSize);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    private Notification CreateNotification(string title, NotificationType type = NotificationType.PriceAlert)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Title = title,
            Message = "Test message",
            Type = type
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
