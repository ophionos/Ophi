using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Account;

public class UpdateProfileHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly UpdateProfile.Handler _handler;

    public UpdateProfileHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _handler = new UpdateProfile.Handler(
            _dbContext,
            _passwordHasherMock.Object,
            NullLogger<UpdateProfile.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_NameOnlyChange_UpdatesWithoutPassword()
    {
        var user = SeedUser("original@example.com");
        var command = new UpdateProfile.Command("New Name", user.Email, CurrentPassword: null) { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = _dbContext.Users.First(u => u.Id == user.Id);
        updated.Name.Should().Be("New Name");
        updated.Email.Should().Be("original@example.com");
    }

    [Fact]
    public async Task Handle_EmailChange_WithCorrectPassword_UpdatesEmail()
    {
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var command = new UpdateProfile.Command(user.Name, "Fresh@Example.com", "Password1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.Users.First(u => u.Id == user.Id).Email.Should().Be("fresh@example.com");
    }

    [Fact]
    public async Task Handle_EmailChange_WithoutPassword_ThrowsBadRequest()
    {
        var user = SeedUser("original@example.com");
        var command = new UpdateProfile.Command(user.Name, "fresh@example.com", CurrentPassword: null) { UserId = user.Id };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        var exception = await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Current password is required to change email");
        exception.Which.StatusCode.Should().Be(400);
        _dbContext.Users.First(u => u.Id == user.Id).Email.Should().Be("original@example.com");
    }

    [Fact]
    public async Task Handle_EmailChange_WithWrongPassword_ThrowsUnauthorized()
    {
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Failed);
        var command = new UpdateProfile.Command(user.Name, "fresh@example.com", "WrongPassword1") { UserId = user.Id };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Current password is incorrect");
        _dbContext.Users.First(u => u.Id == user.Id).Email.Should().Be("original@example.com");
    }

    [Fact]
    public async Task Handle_EmailChange_ToTakenEmail_ThrowsConflict()
    {
        SeedUser("taken@example.com");
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var command = new UpdateProfile.Command(user.Name, "taken@example.com", "Password1") { UserId = user.Id };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        var exception = await act.Should().ThrowAsync<ApiException>()
            .WithMessage("This email is already in use");
        exception.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_EmailCasingOnlyChange_DoesNotRequirePassword()
    {
        // A differently-cased version of the user's own email normalizes to no change.
        var user = SeedUser("original@example.com");
        var command = new UpdateProfile.Command("New Name", "Original@Example.COM", CurrentPassword: null) { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = _dbContext.Users.First(u => u.Id == user.Id);
        updated.Name.Should().Be("New Name");
        updated.Email.Should().Be("original@example.com");
    }

    [Fact]
    public async Task Handle_DoesNotRotateSecurityStamp()
    {
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var originalStamp = user.SecurityStamp;
        var command = new UpdateProfile.Command("New Name", "fresh@example.com", "Password1") { UserId = user.Id };

        var response = await _handler.Handle(command, TestContext.Current.CancellationToken);

        response.SecurityStamp.Should().Be(originalStamp);
        _dbContext.Users.First(u => u.Id == user.Id).SecurityStamp.Should().Be(originalStamp);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsUnauthorized()
    {
        var command = new UpdateProfile.Command("Name", "any@example.com", null) { UserId = Guid.NewGuid() };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    private User SeedUser(string email, PasswordVerificationResult? verifyResult = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = "Test User",
            PasswordHash = $"hash-{email}"
        };
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();

        if (verifyResult is not null)
        {
            _passwordHasherMock
                .Setup(x => x.VerifyHashedPassword(It.IsAny<User>(), user.PasswordHash, It.IsAny<string>()))
                .Returns(verifyResult.Value);
        }

        return user;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
