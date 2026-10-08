using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Ophi.Api.Features.Auth;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Auth;

public class ForgotPasswordHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly ForgotPassword.SendResetEmailHandler _handler;

    public ForgotPasswordHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _emailServiceMock = new Mock<IEmailService>();
        _handler = new ForgotPassword.SendResetEmailHandler(_dbContext, _emailServiceMock.Object, TimeProvider.System, NullLogger<ForgotPassword.SendResetEmailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithExistingEmail_SendsResetEmail()
    {
        SeedUser("user@example.com");
        var command = new ForgotPassword.SendResetEmail("user@example.com");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _emailServiceMock.Verify(
            x => x.SendPasswordResetAsync("user@example.com", It.Is<string>(t => t.Length == 64), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithExistingEmail_SetsResetTokenOnUser()
    {
        var user = SeedUser("user@example.com");
        var command = new ForgotPassword.SendResetEmail("user@example.com");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updatedUser = _dbContext.Users.First(u => u.Id == user.Id);
        updatedUser.PasswordResetTokenHash.Should().NotBeNullOrEmpty();
        updatedUser.PasswordResetTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_DoesNotThrow()
    {
        var command = new ForgotPassword.SendResetEmail("nobody@example.com");

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_DoesNotSendEmail()
    {
        var command = new ForgotPassword.SendResetEmail("nobody@example.com");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _emailServiceMock.Verify(
            x => x.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithExistingToken_OverwritesPreviousToken()
    {
        var user = SeedUser("user@example.com");
        user.PasswordResetTokenHash = "OLD_HASH";
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        _dbContext.SaveChanges();

        var command = new ForgotPassword.SendResetEmail("user@example.com");
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updatedUser = _dbContext.Users.First(u => u.Id == user.Id);
        updatedUser.PasswordResetTokenHash.Should().NotBe("OLD_HASH");
    }

    [Fact]
    public async Task Handle_WithUppercaseEmail_NormalizesToLowercase()
    {
        SeedUser("user@example.com");
        var command = new ForgotPassword.SendResetEmail("USER@EXAMPLE.COM");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _emailServiceMock.Verify(
            x => x.SendPasswordResetAsync("user@example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private User SeedUser(string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = "Test User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();
        return user;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
