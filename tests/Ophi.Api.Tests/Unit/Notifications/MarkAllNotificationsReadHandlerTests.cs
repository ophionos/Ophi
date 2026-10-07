using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Features.Notifications;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Notifications;

public class MarkAllNotificationsReadHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly MarkAllNotificationsRead.Handler _handler;
    private readonly Guid _testUserId;

    public MarkAllNotificationsReadHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new MarkAllNotificationsRead.Handler(_dbContext, NullLogger<MarkAllNotificationsRead.Handler>.Instance);
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
    public async Task Handle_MarksAllUnreadAsRead()
    {
        _dbContext.Notifications.AddRange(
            CreateNotification(false),
            CreateNotification(false),
            CreateNotification(false));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkAllNotificationsRead.Command(_testUserId);

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.ChangeTracker.Clear();
        var notifications = await _dbContext.Notifications
            .Where(n => n.UserId == _testUserId)
            .ToListAsync(TestContext.Current.CancellationToken);
        notifications.Should().AllSatisfy(n => n.IsRead.Should().BeTrue());
    }

    [Fact]
    public async Task Handle_WithNoUnread_DoesNotThrow()
    {
        _dbContext.Notifications.Add(CreateNotification(true));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkAllNotificationsRead.Command(_testUserId);

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_OnlyAffectsCurrentUser()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other",
            PasswordHash = "hash"
        });

        var otherNotification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Title = "Other's",
            Message = "msg",
            Type = NotificationType.PriceAlert
        };
        _dbContext.Notifications.AddRange(
            CreateNotification(false),
            otherNotification);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkAllNotificationsRead.Command(_testUserId);

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.ChangeTracker.Clear();
        var other = await _dbContext.Notifications.FindAsync([otherNotification.Id], TestContext.Current.CancellationToken);
        other!.IsRead.Should().BeFalse();
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
