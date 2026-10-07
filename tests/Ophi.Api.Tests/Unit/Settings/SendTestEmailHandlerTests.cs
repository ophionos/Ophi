using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Settings;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Settings;

public class SendTestEmailHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Guid _testUserId;

    public SendTestEmailHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _emailServiceMock = new Mock<IEmailService>();
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

    private SendTestEmail.Handler CreateHandler(EmailSettings settings) =>
        new(_dbContext, _emailServiceMock.Object, Options.Create(settings), NullLogger<SendTestEmail.Handler>.Instance);

    private static EmailSettings Configured() => new()
    {
        SmtpHost = "smtp.test.com",
        SmtpUser = "smtpuser",
        SmtpPass = "smtppass",
        FromEmail = "noreply@ophi.app"
    };

    // The security assertion: the handler must resolve the recipient from the authenticated
    // user's stored account email and nowhere else. Assert the exact address — It.IsAny<string>()
    // would pass even if the handler took a recipient from the request.
    [Fact]
    public async Task Handle_WithSmtpConfigured_SendsOnlyToTheAuthenticatedUsersOwnEmail()
    {
        var handler = CreateHandler(Configured());

        var result = await handler.Handle(new SendTestEmail.Command { UserId = _testUserId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        _emailServiceMock.Verify(
            e => e.SendTestEmailAsync("test@example.com", "Test User", It.IsAny<CancellationToken>()),
            Times.Once);
        _emailServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WithSmtpConfigured_ReportsTheAddressItSentTo()
    {
        var handler = CreateHandler(Configured());

        var result = await handler.Handle(new SendTestEmail.Command { UserId = _testUserId }, TestContext.Current.CancellationToken);

        result.SentTo.Should().Be("test@example.com");
    }

    [Fact]
    public async Task Handle_WithSmtpNotConfigured_ReturnsErrorNamingTheEnvironmentVariables()
    {
        var handler = CreateHandler(new EmailSettings());

        var result = await handler.Handle(new SendTestEmail.Command { UserId = _testUserId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SMTP_HOST");
        _emailServiceMock.Verify(
            e => e.SendTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSendingFails_ReturnsTheErrorInsteadOfThrowing()
    {
        _emailServiceMock
            .Setup(e => e.SendTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("535 authentication failed"));
        var handler = CreateHandler(Configured());

        var result = await handler.Handle(new SendTestEmail.Command { UserId = _testUserId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("535 authentication failed");
    }

    // The SMTP password is never part of an exception message MailKit produces, but the response
    // must not carry it even if a future failure path tries to.
    [Fact]
    public async Task Handle_WhenSendingFails_DoesNotLeakSmtpCredentials()
    {
        _emailServiceMock
            .Setup(e => e.SendTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection refused"));
        var handler = CreateHandler(Configured());

        var result = await handler.Handle(new SendTestEmail.Command { UserId = _testUserId }, TestContext.Current.CancellationToken);

        result.Error.Should().NotContain("smtppass");
        result.Error.Should().NotContain("smtpuser");
    }

    [Fact]
    public async Task Handle_WithNonExistentUser_ThrowsNotFound()
    {
        var handler = CreateHandler(Configured());

        var act = () => handler.Handle(new SendTestEmail.Command { UserId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Guards the open-relay shape: the command carries the user identity and nothing else, so no
    // caller can nominate a recipient. If a recipient property is ever added, this fails.
    [Fact]
    public void Command_CarriesNoRecipientAddress()
    {
        typeof(SendTestEmail.Command).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo([nameof(SendTestEmail.Command.UserId)]);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
