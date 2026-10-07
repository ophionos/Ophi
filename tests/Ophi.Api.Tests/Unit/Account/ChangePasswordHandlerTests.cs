using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Account;

public class ChangePasswordHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly ChangePassword.Handler _handler;

    public ChangePasswordHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), It.IsAny<string>()))
            .Returns("new_hashed_password");
        _handler = new ChangePassword.Handler(
            _dbContext,
            _passwordHasherMock.Object,
            new SecurityStampGuard(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<ChangePassword.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithCorrectCurrentPassword_UpdatesHash()
    {
        var user = SeedUser(verifyResult: PasswordVerificationResult.Success);
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.Users.First(u => u.Id == user.Id).PasswordHash.Should().Be("new_hashed_password");
    }

    [Fact]
    public async Task Handle_WithCorrectCurrentPassword_RotatesSecurityStamp()
    {
        var user = SeedUser(verifyResult: PasswordVerificationResult.Success);
        var originalStamp = user.SecurityStamp;
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.Users.First(u => u.Id == user.Id).SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public async Task Handle_ReturnsIdentityForCookieReissue()
    {
        var user = SeedUser(verifyResult: PasswordVerificationResult.Success);
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1") { UserId = user.Id };

        var response = await _handler.Handle(command, TestContext.Current.CancellationToken);

        response.Id.Should().Be(user.Id);
        response.Email.Should().Be(user.Email);
        response.SecurityStamp.Should().Be(_dbContext.Users.First(u => u.Id == user.Id).SecurityStamp);
    }

    [Fact]
    public async Task Handle_WithRehashNeededResult_StillSucceeds()
    {
        var user = SeedUser(verifyResult: PasswordVerificationResult.SuccessRehashNeeded);
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.Users.First(u => u.Id == user.Id).PasswordHash.Should().Be("new_hashed_password");
    }

    [Fact]
    public async Task Handle_WithWrongCurrentPassword_ThrowsUnauthorizedAndChangesNothing()
    {
        var user = SeedUser(verifyResult: PasswordVerificationResult.Failed);
        var originalStamp = user.SecurityStamp;
        var command = new ChangePassword.Command("WrongPassword1", "NewPassword1") { UserId = user.Id };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Current password is incorrect");
        var unchanged = _dbContext.Users.First(u => u.Id == user.Id);
        unchanged.PasswordHash.Should().Be("old_hash");
        unchanged.SecurityStamp.Should().Be(originalStamp);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsUnauthorized()
    {
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1") { UserId = Guid.NewGuid() };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    private User SeedUser(PasswordVerificationResult verifyResult)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@example.com",
            Name = "Test User",
            PasswordHash = "old_hash"
        };
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(It.IsAny<User>(), "old_hash", It.IsAny<string>()))
            .Returns(verifyResult);
        return user;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
