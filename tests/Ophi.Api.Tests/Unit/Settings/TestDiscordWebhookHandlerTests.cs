using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Settings;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Settings;

public class TestDiscordWebhookHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IDiscordService> _discordServiceMock;
    private readonly TestDiscordWebhook.Handler _handler;
    private readonly Guid _testUserId;

    public TestDiscordWebhookHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _discordServiceMock = new Mock<IDiscordService>();
        _handler = new TestDiscordWebhook.Handler(_dbContext, _discordServiceMock.Object, NullLogger<TestDiscordWebhook.Handler>.Instance);
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
    public async Task Handle_WithConfiguredWebhookUrl_SendsTestMessage()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new TestDiscordWebhook.Command { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        _discordServiceMock.Verify(
            d => d.SendPriceAlertAsync(
                It.Is<DiscordPriceAlert>(a => a.ProductName == "Test Product"),
                "https://discord.com/api/webhooks/123/abc",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNoWebhookUrl_ReturnsError()
    {
        var command = new TestDiscordWebhook.Command { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("webhook URL");
    }

    [Fact]
    public async Task Handle_WhenDiscordFails_ReturnsError()
    {
        var user = _dbContext.Users.First(u => u.Id == _testUserId);
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _discordServiceMock
            .Setup(d => d.SendPriceAlertAsync(It.IsAny<DiscordPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Discord unavailable"));

        var command = new TestDiscordWebhook.Command { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Discord unavailable");
    }

    [Fact]
    public async Task Handle_WithNonExistentUser_ThrowsNotFound()
    {
        var command = new TestDiscordWebhook.Command { UserId = Guid.NewGuid() };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
