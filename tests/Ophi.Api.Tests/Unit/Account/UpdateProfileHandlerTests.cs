using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;
using Wolverine;

namespace Ophi.Api.Tests.Unit.Account;

public class UpdateProfileHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly UpdateProfile.Handler _handler;
    private readonly UpdateProfile.Handler _smtpHandler;

    public UpdateProfileHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _handler = CreateHandler(new EmailSettings());
        _smtpHandler = CreateHandler(new EmailSettings { SmtpHost = "smtp.example.com" });
    }

    private UpdateProfile.Handler CreateHandler(EmailSettings emailSettings) => new(
        _dbContext,
        _passwordHasherMock.Object,
        Options.Create(emailSettings),
        _busMock.Object,
        NullLogger<UpdateProfile.Handler>.Instance);

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
    public async Task Handle_EmailChange_WithoutSmtp_ClearsPendingResetToken()
    {
        // A reset link already mailed to the old address must not outlive the address change.
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        user.PasswordResetTokenHash = "old-reset-hash";
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        _dbContext.SaveChanges();
        var command = new UpdateProfile.Command(user.Name, "fresh@example.com", "Password1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = _dbContext.Users.First(u => u.Id == user.Id);
        updated.PasswordResetTokenHash.Should().BeNull();
        updated.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmailChange_WithSmtp_KeepsEmailAndSetsPendingEmail()
    {
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var command = new UpdateProfile.Command("New Name", "Fresh@Example.com", "Password1") { UserId = user.Id };

        var response = await _smtpHandler.Handle(command, TestContext.Current.CancellationToken);

        var updated = _dbContext.Users.First(u => u.Id == user.Id);
        updated.Email.Should().Be("original@example.com");
        updated.PendingEmail.Should().Be("fresh@example.com");
        updated.Name.Should().Be("New Name");
        response.Email.Should().Be("original@example.com");
        response.PendingEmail.Should().Be("fresh@example.com");
    }

    [Fact]
    public async Task Handle_EmailChange_WithSmtp_PublishesConfirmationWithoutAToken()
    {
        // The token is generated in the background handler: a raw token on a durable message would
        // sit in the Wolverine envelope tables (and in every database dump).
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var command = new UpdateProfile.Command(user.Name, "fresh@example.com", "Password1") { UserId = user.Id };

        await _smtpHandler.Handle(command, TestContext.Current.CancellationToken);

        _busMock.Verify(
            x => x.PublishAsync(new UpdateProfile.SendEmailChangeConfirmation(user.Id, "fresh@example.com"), It.IsAny<DeliveryOptions?>()),
            Times.Once);
        _dbContext.Users.First(u => u.Id == user.Id).EmailChangeTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NameOnlyChange_WithSmtp_KeepsPendingEmailAndPublishesNothing()
    {
        var user = SeedUser("original@example.com");
        user.RequestEmailChange("pending@example.com");
        _dbContext.SaveChanges();
        var command = new UpdateProfile.Command("New Name", user.Email, CurrentPassword: null) { UserId = user.Id };

        var response = await _smtpHandler.Handle(command, TestContext.Current.CancellationToken);

        response.PendingEmail.Should().Be("pending@example.com");
        _busMock.Verify(x => x.PublishAsync(It.IsAny<UpdateProfile.SendEmailChangeConfirmation>(), It.IsAny<DeliveryOptions?>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailChange_WithSmtp_ToTakenEmail_ThrowsConflictAndSetsNothing()
    {
        SeedUser("taken@example.com");
        var user = SeedUser("original@example.com", verifyResult: PasswordVerificationResult.Success);
        var command = new UpdateProfile.Command(user.Name, "taken@example.com", "Password1") { UserId = user.Id };

        var act = () => _smtpHandler.Handle(command, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(409);
        _dbContext.ChangeTracker.Clear();
        _dbContext.Users.First(u => u.Id == user.Id).PendingEmail.Should().BeNull();
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
