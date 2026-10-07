using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Auth;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Auth;

public class ResetPasswordHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly ResetPassword.Handler _handler;

    public ResetPasswordHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), It.IsAny<string>()))
            .Returns("new_hashed_password");
        _handler = new ResetPassword.Handler(
            _dbContext,
            _passwordHasherMock.Object,
            TimeProvider.System,
            new SecurityStampGuard(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<ResetPassword.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithValidToken_ResetsPassword()
    {
        var (rawToken, user) = SeedUserWithResetToken();
        var command = new ResetPassword.Command(rawToken, "NewPassword1");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updatedUser = _dbContext.Users.First(u => u.Id == user.Id);
        updatedUser.PasswordHash.Should().Be("new_hashed_password");
    }

    [Fact]
    public async Task Handle_WithValidToken_ClearsResetTokenFields()
    {
        var (rawToken, user) = SeedUserWithResetToken();
        var command = new ResetPassword.Command(rawToken, "NewPassword1");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updatedUser = _dbContext.Users.First(u => u.Id == user.Id);
        updatedUser.PasswordResetTokenHash.Should().BeNull();
        updatedUser.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithValidToken_RotatesSecurityStamp()
    {
        var (rawToken, user) = SeedUserWithResetToken();
        var originalStamp = user.SecurityStamp;
        var command = new ResetPassword.Command(rawToken, "NewPassword1");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updatedUser = _dbContext.Users.First(u => u.Id == user.Id);
        updatedUser.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public async Task Handle_WithValidToken_CallsPasswordHasher()
    {
        var (rawToken, _) = SeedUserWithResetToken();
        var command = new ResetPassword.Command(rawToken, "NewPassword1");

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _passwordHasherMock.Verify(
            x => x.HashPassword(It.IsAny<User>(), "NewPassword1"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithExpiredToken_ThrowsApiException()
    {
        var (rawToken, _) = SeedUserWithResetToken(expiresAt: DateTime.UtcNow.AddHours(-1));
        var command = new ResetPassword.Command(rawToken, "NewPassword1");

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Invalid or expired reset token");
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ThrowsApiException()
    {
        SeedUserWithResetToken();
        var command = new ResetPassword.Command("totally_wrong_token", "NewPassword1");

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Invalid or expired reset token");
    }

    [Fact]
    public async Task Handle_WithNoMatchingUser_ThrowsApiException()
    {
        var command = new ResetPassword.Command("some_random_token", "NewPassword1");

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Invalid or expired reset token");
    }

    private (string rawToken, User user) SeedUserWithResetToken(DateTime? expiresAt = null)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hashedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            Name = "Test User",
            PasswordHash = "old_hash",
            PasswordResetTokenHash = hashedToken,
            PasswordResetTokenExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1)
        };
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();
        return (rawToken, user);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
