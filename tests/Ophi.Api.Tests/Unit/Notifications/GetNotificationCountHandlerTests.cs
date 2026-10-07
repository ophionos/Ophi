using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Notifications;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Notifications;

public class GetNotificationCountHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetNotificationCount.Handler _handler;
    private readonly Guid _testUserId;

    public GetNotificationCountHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetNotificationCount.Handler(_dbContext, NullLogger<GetNotificationCount.Handler>.Instance);
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
    public async Task Handle_WithNoNotifications_ReturnsZero()
    {
        var query = new GetNotificationCount.Query(_testUserId);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Unread.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithMixedReadUnread_ReturnsUnreadCount()
    {
        var unread1 = CreateNotification(false);
        var unread2 = CreateNotification(false);
        var read = CreateNotification(true);
        _dbContext.Notifications.AddRange(unread1, unread2, read);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotificationCount.Query(_testUserId);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Unread.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OnlyCountsCurrentUserNotifications()
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
            CreateNotification(false),
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                Title = "Other's",
                Message = "msg",
                Type = NotificationType.PriceAlert
            });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetNotificationCount.Query(_testUserId);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Unread.Should().Be(1);
    }

    private Notification CreateNotification(bool isRead)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Title = "Test",
            Message = "msg",
            Type = NotificationType.PriceAlert,
            IsRead = isRead
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
