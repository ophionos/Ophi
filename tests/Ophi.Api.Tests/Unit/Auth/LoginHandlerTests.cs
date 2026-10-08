using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Auth;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Auth;

public class LoginHandlerTests : IDisposable
{
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Login.Handler _handler;

    public LoginHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _handler = new Login.Handler(
            _dbContext,
            _passwordHasherMock.Object,
            Options.Create(new PasswordHasherOptions()),
            TimeProvider.System,
            NullLogger<Login.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithUnknownEmail_RunsAVerifyThroughTheConfiguredHasher()
    {
        // The unknown-email path must spend the same PBKDF2 work as a known one, using the
        // application's configured hasher — not a locally-constructed one whose iteration count is
        // baked in at default and would drift the moment PasswordHasherOptions is tuned.
        var command = new Login.Command("nobody@example.com", "whatever");

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — still a generic failure, but the work was done on the injected hasher.
        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid email or password");

        _passwordHasherMock.Verify(
            x => x.VerifyHashedPassword(It.IsAny<User>(), It.IsAny<string>(), command.Password),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsLoginResponse()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hashed_password"
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Login.Command("test@example.com", "correct_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password))
            .Returns(PasswordVerificationResult.Success);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
        result.Name.Should().Be(user.Name);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_UpdatesLastLoginAt()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hashed_password",
            LastLoginAt = null
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Login.Command("test@example.com", "correct_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password))
            .Returns(PasswordVerificationResult.Success);

        var beforeLogin = DateTime.UtcNow;

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedUser = await _dbContext.Users.FindAsync([user.Id], TestContext.Current.CancellationToken);
        updatedUser!.LastLoginAt.Should().NotBeNull();
        updatedUser.LastLoginAt.Should().BeOnOrAfter(beforeLogin);
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ThrowsUnauthorizedException()
    {
        // Arrange
        var command = new Login.Command("nonexistent@example.com", "password");

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task Handle_WithIncorrectPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hashed_password"
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Login.Command("test@example.com", "wrong_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password))
            .Returns(PasswordVerificationResult.Failed);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task Handle_WithEmailDifferentCase_FindsUser()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hashed_password"
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Login.Command("TEST@EXAMPLE.COM", "correct_password"); // Different case

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password))
            .Returns(PasswordVerificationResult.Success);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_CallsPasswordVerifier()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hashed_password"
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Login.Command("test@example.com", "correct_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password))
            .Returns(PasswordVerificationResult.Success);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        _passwordHasherMock.Verify(
            x => x.VerifyHashedPassword(user, user.PasswordHash, command.Password),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_WhenRehashNeeded_StoresNewHashWithoutRotatingSecurityStamp()
    {
        // A hash made under older PBKDF2 parameters is upgraded on the next good login. The stamp
        // stays, so the upgrade does not sign the user out of their other sessions.
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com", Name = "Test User", PasswordHash = "old_hash" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stamp = user.SecurityStamp;
        var command = new Login.Command("test@example.com", "correct_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, "old_hash", command.Password))
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);
        _passwordHasherMock
            .Setup(x => x.HashPassword(user, command.Password))
            .Returns("new_hash");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var saved = _dbContext.Users.AsNoTracking().Single(u => u.Id == user.Id);
        saved.PasswordHash.Should().Be("new_hash");
        saved.SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public async Task Handle_WhenHashIsCurrent_DoesNotRehash()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com", Name = "Test User", PasswordHash = "hash" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var command = new Login.Command("test@example.com", "correct_password");

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, "hash", command.Password))
            .Returns(PasswordVerificationResult.Success);

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _passwordHasherMock.Verify(x => x.HashPassword(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
