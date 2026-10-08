using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Account;

/// <summary>
/// The verified email change: the background send (token issue) and the token confirmation.
/// The request side lives in <see cref="UpdateProfileHandlerTests"/>.
/// </summary>
public class EmailChangeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly UpdateProfile.SendEmailChangeConfirmationHandler _sendHandler;
    private readonly ConfirmEmailChange.Handler _confirmHandler;

    public EmailChangeTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _sendHandler = new UpdateProfile.SendEmailChangeConfirmationHandler(
            _dbContext, _emailServiceMock.Object, _time, NullLogger<UpdateProfile.SendEmailChangeConfirmationHandler>.Instance);
        _confirmHandler = new ConfirmEmailChange.Handler(
            _dbContext, _time, NullLogger<ConfirmEmailChange.Handler>.Instance);
    }

    #region Send confirmation

    [Fact]
    public async Task SendConfirmation_WithPendingEmail_SendsTokenToNewAddressAndNoticeToOldAddress()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com");

        await _sendHandler.Handle(new UpdateProfile.SendEmailChangeConfirmation(user.Id, "new@example.com"), TestContext.Current.CancellationToken);

        _emailServiceMock.Verify(
            x => x.SendEmailChangeConfirmationAsync("new@example.com", It.Is<string>(t => t.Length == 64), It.IsAny<CancellationToken>()),
            Times.Once);
        _emailServiceMock.Verify(
            x => x.SendEmailChangeNoticeAsync("old@example.com", "new@example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendConfirmation_WithPendingEmail_StoresOnlyTheTokenHashWithA24HourExpiry()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com");
        string? sentToken = null;
        _emailServiceMock
            .Setup(x => x.SendEmailChangeConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, token, _) => sentToken = token);

        await _sendHandler.Handle(new UpdateProfile.SendEmailChangeConfirmation(user.Id, "new@example.com"), TestContext.Current.CancellationToken);

        var updated = Reload(user.Id);
        updated.EmailChangeTokenHash.Should().Be(Hash(sentToken!));
        updated.EmailChangeTokenExpiresAt.Should().Be(_time.GetUtcNow().UtcDateTime.AddHours(24));
    }

    [Fact]
    public async Task SendConfirmation_WhenPendingEmailChangedSincePublish_SendsNothing()
    {
        // A second change request replaced the first; the stale message must not mail a token
        // that confirms the newer address to the older one.
        var user = SeedUser("old@example.com", pendingEmail: "second@example.com");

        await _sendHandler.Handle(new UpdateProfile.SendEmailChangeConfirmation(user.Id, "first@example.com"), TestContext.Current.CancellationToken);

        _emailServiceMock.VerifyNoOtherCalls();
        Reload(user.Id).EmailChangeTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task SendConfirmation_ForDeletedUser_SendsNothing()
    {
        await _sendHandler.Handle(new UpdateProfile.SendEmailChangeConfirmation(Guid.NewGuid(), "new@example.com"), TestContext.Current.CancellationToken);

        _emailServiceMock.VerifyNoOtherCalls();
    }

    #endregion

    #region Confirm

    [Fact]
    public async Task Confirm_WithValidToken_ChangesEmailAndClearsPendingState()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");

        var response = await _confirmHandler.Handle(new ConfirmEmailChange.Command("valid-token"), TestContext.Current.CancellationToken);

        response.Email.Should().Be("new@example.com");
        response.Id.Should().Be(user.Id);
        var updated = Reload(user.Id);
        updated.Email.Should().Be("new@example.com");
        updated.PendingEmail.Should().BeNull();
        updated.EmailChangeTokenHash.Should().BeNull();
        updated.EmailChangeTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_WithValidToken_ClearsPasswordResetToken()
    {
        // A reset link already mailed to the old address must not outlive the address change.
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");
        user.PasswordResetTokenHash = "reset-hash";
        user.PasswordResetTokenExpiresAt = _time.GetUtcNow().UtcDateTime.AddHours(1);
        _dbContext.SaveChanges();

        await _confirmHandler.Handle(new ConfirmEmailChange.Command("valid-token"), TestContext.Current.CancellationToken);

        var updated = Reload(user.Id);
        updated.PasswordResetTokenHash.Should().BeNull();
        updated.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_WithValidToken_KeepsSecurityStamp()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");
        var stamp = user.SecurityStamp;

        var response = await _confirmHandler.Handle(new ConfirmEmailChange.Command("valid-token"), TestContext.Current.CancellationToken);

        response.SecurityStamp.Should().Be(stamp);
        Reload(user.Id).SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public async Task Confirm_WithExpiredToken_ThrowsBadRequestAndKeepsEmail()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");
        _time.Advance(TimeSpan.FromHours(25));

        var act = () => _confirmHandler.Handle(new ConfirmEmailChange.Command("valid-token"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(400);
        Reload(user.Id).Email.Should().Be("old@example.com");
    }

    [Fact]
    public async Task Confirm_WithUnknownToken_ThrowsBadRequest()
    {
        SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");

        var act = () => _confirmHandler.Handle(new ConfirmEmailChange.Command("other-token"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Confirm_WhenNewEmailWasTakenMeanwhile_ThrowsConflictAndKeepsEmail()
    {
        var user = SeedUser("old@example.com", pendingEmail: "new@example.com", token: "valid-token");
        SeedUser("new@example.com");

        var act = () => _confirmHandler.Handle(new ConfirmEmailChange.Command("valid-token"), TestContext.Current.CancellationToken);

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(409);
        exception.Which.ErrorCode.Should().Be("EmailInUse");
        Reload(user.Id).Email.Should().Be("old@example.com");
    }

    #endregion

    private User SeedUser(string email, string? pendingEmail = null, string? token = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = "Test User",
            PasswordHash = "hash"
        };
        if (pendingEmail is not null)
        {
            user.RequestEmailChange(pendingEmail);
        }
        if (token is not null)
        {
            user.IssueEmailChangeToken(Hash(token), _time.GetUtcNow().UtcDateTime.AddHours(24));
        }
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();
        return user;
    }

    private User Reload(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Users.First(u => u.Id == id);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
