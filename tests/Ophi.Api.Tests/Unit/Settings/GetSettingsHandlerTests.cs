using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Settings;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Settings;

public class GetSettingsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetSettings.Handler _handler;
    private readonly Guid _testUserId;

    public GetSettingsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = CreateHandler(new EmailSettings { SmtpHost = "smtp.test.com" });
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

    private GetSettings.Handler CreateHandler(EmailSettings emailSettings) =>
        new(_dbContext, Options.Create(emailSettings), new Moq.Mock<Ophi.Infrastructure.Push.ITelegramService>().Object,
            new Moq.Mock<Ophi.Infrastructure.Push.IPushoverService>().Object, NullLogger<GetSettings.Handler>.Instance);

    // SMTP is server-level config (appsettings/env), not a per-user field, so the settings
    // response exposes only whether it is configured — never the host, user or password.
    [Fact]
    public async Task Handle_WithSmtpConfigured_ReportsEmailConfigured()
    {
        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.EmailConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNoSmtpHost_ReportsEmailNotConfigured()
    {
        var handler = CreateHandler(new EmailSettings());
        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await handler.Handle(query, TestContext.Current.CancellationToken);

        result.EmailConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsUserSettings()
    {
        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AffiliatesEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithAffiliatesDisabled_ReturnsFalse()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.AffiliatesEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AffiliatesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsDefaultCheckIntervalMinutes()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DefaultCheckIntervalMinutes = 120;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.DefaultCheckIntervalMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Handle_WithNoDefaultCheckInterval_ReturnsNull()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.DefaultCheckIntervalMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsPageFetchDelaySeconds()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.PageFetchDelaySeconds = 5;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.PageFetchDelaySeconds.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WithNoPageFetchDelay_ReturnsNull()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.PageFetchDelaySeconds.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsScrapeCacheTtlMinutes()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.ScrapeCacheTtlMinutes = 30;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.ScrapeCacheTtlMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Handle_WithNoScrapeCacheTtl_ReturnsNull()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.ScrapeCacheTtlMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsDiscordWebhookUrl()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        user.DiscordNotificationsEnabled = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.DiscordWebhookConfigured.Should().BeTrue();
        result.DiscordNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNoDiscordSettings_ReturnsDefaults()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.DiscordWebhookConfigured.Should().BeFalse();
        result.DiscordNotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsAnomalyThresholdPercent()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.AnomalyThresholdPercent = 70;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AnomalyThresholdPercent.Should().Be(70);
    }

    [Fact]
    public async Task Handle_WithNoAnomalyThreshold_ReturnsNull()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AnomalyThresholdPercent.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsAutoPauseAfterFailures()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.AutoPauseAfterFailures = 10;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AutoPauseAfterFailures.Should().Be(10);
    }

    [Fact]
    public async Task Handle_WithNoAutoPause_ReturnsNull()
    {
        var query = new GetSettings.Query { UserId = _testUserId };
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.AutoPauseAfterFailures.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentUser_ThrowsNotFound()
    {
        var query = new GetSettings.Query { UserId = Guid.NewGuid() };

        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Handle_ByDefault_ReportsEmailNotificationsEnabled()
    {
        // Email is the channel the product promises, so an account that has never touched the
        // setting must read as opted in.
        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.EmailNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithEmailNotificationsDisabled_ReportsDisabled()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.EmailNotificationsEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.EmailNotificationsEnabled.Should().BeFalse();
    }

    // The user preference and the server's SMTP state are independent facts. A user can opt out
    // on a server with working SMTP, and an opted-in user can sit on a server with none.
    [Fact]
    public async Task Handle_WithSmtpUnconfigured_StillReportsTheUserPreference()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.EmailNotificationsEnabled = false;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(new EmailSettings());
        var query = new GetSettings.Query { UserId = _testUserId };

        var result = await handler.Handle(query, TestContext.Current.CancellationToken);

        result.EmailConfigured.Should().BeFalse();
        result.EmailNotificationsEnabled.Should().BeFalse();
    }
}
