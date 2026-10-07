using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Settings;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Settings;

public class UpdateSettingsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly UpdateSettings.Handler _handler;
    private readonly Guid _testUserId;

    public UpdateSettingsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new UpdateSettings.Handler(_dbContext, NullLogger<UpdateSettings.Handler>.Instance);
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
    public async Task Handle_DisablesAffiliates_SetsToFalse()
    {
        var command = new UpdateSettings.Command(AffiliatesEnabled: false, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.AffiliatesEnabled.Should().BeFalse();
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.AffiliatesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EnablesAffiliates_SetsToTrue()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.AffiliatesEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(AffiliatesEnabled: true, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.AffiliatesEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UpdatesDefaultCheckIntervalMinutes()
    {
        var command = new UpdateSettings.Command(null, 120) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DefaultCheckIntervalMinutes.Should().Be(120);
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DefaultCheckIntervalMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Handle_ClearsDefaultCheckInterval_WhenSetToZero()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DefaultCheckIntervalMinutes = 120;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(null, 0) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DefaultCheckIntervalMinutes.Should().BeNull();
        var updated = _dbContext.Users.First(u => u.Id == _testUserId);
        updated.DefaultCheckIntervalMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DoesNotChangeAffiliates_WhenOnlyIntervalProvided()
    {
        var command = new UpdateSettings.Command(null, 30) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.AffiliatesEnabled.Should().BeTrue(); // unchanged default
        result.DefaultCheckIntervalMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Handle_UpdatesPageFetchDelaySeconds()
    {
        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: 5) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.PageFetchDelaySeconds.Should().Be(5);
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.PageFetchDelaySeconds.Should().Be(5);
    }

    [Fact]
    public async Task Handle_ClearsPageFetchDelay_WhenSetToZero()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.PageFetchDelaySeconds = 10;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: 0) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.PageFetchDelaySeconds.Should().BeNull();
        var updated = _dbContext.Users.First(u => u.Id == _testUserId);
        updated.PageFetchDelaySeconds.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DoesNotChangeDelay_WhenNotProvided()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.PageFetchDelaySeconds = 10;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(AffiliatesEnabled: true, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.PageFetchDelaySeconds.Should().Be(10);
    }

    [Fact]
    public async Task Handle_UpdatesScrapeCacheTtlMinutes()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 30) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.ScrapeCacheTtlMinutes.Should().Be(30);
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.ScrapeCacheTtlMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Handle_ClearsScrapeCacheTtl_WhenSetToZero()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.ScrapeCacheTtlMinutes = 30;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 0) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.ScrapeCacheTtlMinutes.Should().BeNull();
        var updated = _dbContext.Users.First(u => u.Id == _testUserId);
        updated.ScrapeCacheTtlMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DoesNotChangeCacheTtl_WhenNotProvided()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.ScrapeCacheTtlMinutes = 30;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(AffiliatesEnabled: true, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.ScrapeCacheTtlMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Handle_UpdatesDiscordWebhookUrl()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "https://discord.com/api/webhooks/123/abc") { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DiscordWebhookConfigured.Should().BeTrue();
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl.Should().Be("https://discord.com/api/webhooks/123/abc");
    }

    [Fact]
    public async Task Handle_ClearsDiscordWebhookUrl_WhenSetToEmptyString()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "") { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DiscordWebhookConfigured.Should().BeFalse();
        var updated = _dbContext.Users.First(u => u.Id == _testUserId);
        updated.DiscordWebhookUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UpdatesDiscordNotificationsEnabled()
    {
        var command = new UpdateSettings.Command(null, null, DiscordNotificationsEnabled: true) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DiscordNotificationsEnabled.Should().BeTrue();
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotChangeDiscordSettings_WhenNotProvided()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        user.DiscordNotificationsEnabled = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(AffiliatesEnabled: false, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.DiscordWebhookConfigured.Should().BeTrue();
        result.DiscordNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNonExistentUser_ThrowsNotFound()
    {
        var command = new UpdateSettings.Command(null, null) { UserId = Guid.NewGuid() };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Handle_DisablesEmailNotifications_SetsToFalse()
    {
        var command = new UpdateSettings.Command(null, null, EmailNotificationsEnabled: false) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.EmailNotificationsEnabled.Should().BeFalse();
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.EmailNotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReEnablesEmailNotifications_SetsToTrue()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.EmailNotificationsEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(null, null, EmailNotificationsEnabled: true) { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.EmailNotificationsEnabled.Should().BeTrue();
        _dbContext.Users.First(u => u.Id == _testUserId).EmailNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotChangeEmailNotifications_WhenNotProvided()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.EmailNotificationsEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateSettings.Command(AffiliatesEnabled: false, DefaultCheckIntervalMinutes: null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.EmailNotificationsEnabled.Should().BeFalse();
    }
}
