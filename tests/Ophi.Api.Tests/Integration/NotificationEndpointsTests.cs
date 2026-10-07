using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Features.Notifications;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Integration;

public class NotificationEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public NotificationEndpointsTests(OphiWebApplicationFactory factory) : base(factory)
    {
        _client = Factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();
        var email = $"notification-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        // Register
        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        });

        // Login
        await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = email,
            Password = password
        });

        // Get the user ID from the database
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email, TestContext.Current.CancellationToken);

        return (client, user.Id);
    }

    private async Task<Guid> SeedNotificationAsync(Guid userId, string title = "Test Notification", string message = "Test message", bool isRead = false, Guid? productId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Type = NotificationType.PriceAlert,
            IsRead = isRead,
            ProductId = productId
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return notification.Id;
    }

    #region GetNotifications Tests

    [Fact]
    public async Task GetNotifications_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotifications_WithNoNotifications_ReturnsEmptyList()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<GetNotifications.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetNotifications_ReturnsUserNotifications()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();

        var notifId1 = await SeedNotificationAsync(userId, "Alert 1", "Price dropped on product 1", isRead: false);
        var notifId2 = await SeedNotificationAsync(userId, "Alert 2", "Price dropped on product 2", isRead: true);

        // Act
        var response = await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<GetNotifications.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.Items.Should().Contain(n => n.Id == notifId1);
        result.Items.Should().Contain(n => n.Id == notifId2);
    }

    [Fact]
    public async Task GetNotifications_UnreadOnlyFilter_ReturnsOnlyUnread()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();

        var unreadId1 = await SeedNotificationAsync(userId, "Unread 1", "First unread", isRead: false);
        var unreadId2 = await SeedNotificationAsync(userId, "Unread 2", "Second unread", isRead: false);
        var readId = await SeedNotificationAsync(userId, "Read", "Already read", isRead: true);

        // Act
        var response = await client.GetAsync("/api/v1/notifications?unreadOnly=true", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<GetNotifications.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(n => n.IsRead.Should().BeFalse());
        result.Items.Should().Contain(n => n.Id == unreadId1);
        result.Items.Should().Contain(n => n.Id == unreadId2);
        result.Items.Should().NotContain(n => n.Id == readId);
    }

    #endregion

    #region GetNotificationCount Tests

    [Fact]
    public async Task GetNotificationCount_ReturnsUnreadCount()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();

        await SeedNotificationAsync(userId, "Unread 1", "First unread", isRead: false);
        await SeedNotificationAsync(userId, "Unread 2", "Second unread", isRead: false);
        await SeedNotificationAsync(userId, "Read", "Already read", isRead: true);

        // Act
        var response = await client.GetAsync("/api/v1/notifications/count", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<GetNotificationCount.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.Unread.Should().Be(2);
    }

    #endregion

    #region MarkNotificationRead Tests

    [Fact]
    public async Task MarkNotificationRead_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();
        var notificationId = Guid.NewGuid();

        // Act
        var response = await unauthenticatedClient.PutAsync($"/api/v1/notifications/{notificationId}/read", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MarkNotificationRead_UpdatesIsReadFlag()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var notifId = await SeedNotificationAsync(userId, "Unread Alert", "Notification to mark as read", isRead: false);

        // Act — mark as read
        var response = await client.PutAsync($"/api/v1/notifications/{notifId}/read", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the flag was updated in the database
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var notification = await db.Notifications.FirstAsync(n => n.Id == notifId, TestContext.Current.CancellationToken);
        notification.IsRead.Should().BeTrue();
    }

    #endregion

    #region MarkAllNotificationsRead Tests

    [Fact]
    public async Task MarkAllNotificationsRead_MarksAllAsRead()
    {
        // Arrange
        var (client, userId) = await CreateAuthenticatedClientAsync();

        await SeedNotificationAsync(userId, "Unread 1", "First unread", isRead: false);
        await SeedNotificationAsync(userId, "Unread 2", "Second unread", isRead: false);
        await SeedNotificationAsync(userId, "Unread 3", "Third unread", isRead: false);

        // Verify initial state: 3 unread
        var countResponse = await client.GetAsync("/api/v1/notifications/count", TestContext.Current.CancellationToken);
        var countResult = await countResponse.Content.ReadFromJsonAsync<GetNotificationCount.Response>(cancellationToken: TestContext.Current.CancellationToken);
        countResult!.Unread.Should().Be(3);

        // Act — mark all as read
        var response = await client.PostAsync("/api/v1/notifications/read-all", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify all are now marked as read
        var newCountResponse = await client.GetAsync("/api/v1/notifications/count", TestContext.Current.CancellationToken);
        var newCountResult = await newCountResponse.Content.ReadFromJsonAsync<GetNotificationCount.Response>(cancellationToken: TestContext.Current.CancellationToken);
        newCountResult!.Unread.Should().Be(0);
    }

    #endregion
}
