using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Auth;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;

namespace Ophi.Api.Tests.Unit.Auth;

public class RegisterHandlerTests : IDisposable
{
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Register.Handler _handler;

    public RegisterHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _handler = CreateHandler(registrationEnabled: true);
    }

    private Register.Handler CreateHandler(bool registrationEnabled) =>
        new(_dbContext, _passwordHasherMock.Object,
            Options.Create(new RegistrationSettings { Enabled = registrationEnabled }),
            NullLogger<Register.Handler>.Instance);

    [Fact]
    public async Task Handle_WhenRegistrationDisabledAndAUserExists_ThrowsRegistrationDisabled()
    {
        // Arrange
        _dbContext.Users.Add(TestEntityFactory.User().WithEmail("owner@example.com").Build());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(registrationEnabled: false);
        var command = new Register.Command("stranger@example.com", "SecurePass123!", "Stranger");

        // Act
        var act = () => handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.ErrorCode.Should().Be("RegistrationDisabled");
        (await _dbContext.Users.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenRegistrationDisabledAndEmailAlreadyTaken_ThrowsRegistrationDisabled()
    {
        // A closed instance must not reveal which emails have accounts.
        _dbContext.Users.Add(TestEntityFactory.User().WithEmail("owner@example.com").Build());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(registrationEnabled: false);
        var command = new Register.Command("owner@example.com", "SecurePass123!", "Owner");

        var act = () => handler.Handle(command, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.ErrorCode.Should().Be("RegistrationDisabled");
    }

    [Fact]
    public async Task Handle_WhenRegistrationDisabledAndNoUsersExist_CreatesFirstAccount()
    {
        // Arrange
        var handler = CreateHandler(registrationEnabled: false);
        var command = new Register.Command("owner@example.com", "SecurePass123!", "Owner");
        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), command.Password))
            .Returns("hashed_password");

        // Act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Email.Should().Be("owner@example.com");
        (await _dbContext.Users.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithValidData_ReturnsRegisteredUser()
    {
        // Arrange
        var command = new Register.Command(

            "test@example.com",
            "SecurePass123!",
            "Test User"
        );

        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), command.Password))
            .Returns("hashed_password");

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Email.Should().Be("test@example.com");
        result.Name.Should().Be("Test User");
        result.Id.Should().NotBeEmpty();

        // Verify user was saved to database
        var savedUser = await _dbContext.Users.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedUser.Should().NotBeNull();
        savedUser.Email.Should().Be("test@example.com");
        savedUser.PasswordHash.Should().Be("hashed_password");
    }

    [Fact]
    public async Task Handle_WithValidData_NormalizesEmailToLowercase()
    {
        // Arrange
        var command = new Register.Command(

            "TEST@EXAMPLE.COM",
            "SecurePass123!",
            "Test User"
        );

        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), command.Password))
            .Returns("hashed_password");

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Email.Should().Be("test@example.com");

        var savedUser = await _dbContext.Users.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedUser!.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task Handle_WithExistingEmail_ThrowsApiException()
    {
        // Arrange
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            Name = "Existing User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(existingUser);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Register.Command(
            "existing@example.com",
            "SecurePass123!",
            "New User"
        );

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Unable to create account");
    }

    [Fact]
    public async Task Handle_WithExistingEmailDifferentCase_ThrowsApiException()
    {
        // Arrange
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            Name = "Existing User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(existingUser);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Register.Command(
            "EXISTING@EXAMPLE.COM", // Different case
            "SecurePass123!",
            "New User"
        );

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Unable to create account");
    }

    [Fact]
    public async Task Handle_WithValidData_CallsPasswordHasher()
    {
        // Arrange
        var command = new Register.Command(

            "test@example.com",
            "SecurePass123!",
            "Test User"
        );

        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<User>(), command.Password))
            .Returns("hashed_password");

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        _passwordHasherMock.Verify(
            x => x.HashPassword(It.IsAny<User>(), command.Password),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_WithInvalidInput_DoesNotSaveUser()
    {
        // Arrange
        var command = new Register.Command(
            "invalid-email",
            "Short1",
            "Test User"
        );

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<Exception>();

        var userCount = await _dbContext.Users.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        userCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithDuplicateEmail_DoesNotCallPasswordHasher()
    {
        // Arrange
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            Name = "Existing User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(existingUser);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new Register.Command(
            "existing@example.com",
            "SecurePass123!",
            "New User"
        );

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>();

        _passwordHasherMock.Verify(
            x => x.HashPassword(It.IsAny<User>(), It.IsAny<string>()),
            Times.Never
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
