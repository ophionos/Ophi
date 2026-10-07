using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

public class DeleteAccountHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly DeleteAccount.Handler _handler;

    public DeleteAccountHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _handler = new DeleteAccount.Handler(
            _dbContext,
            _passwordHasherMock.Object,
            new SecurityStampGuard(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<DeleteAccount.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithCorrectPassword_RemovesUser()
    {
        var user = SeedUser(PasswordVerificationResult.Success);
        var command = new DeleteAccount.Command("Password1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        _dbContext.Users.Any(u => u.Id == user.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithCorrectPassword_CascadesOwnedData()
    {
        var user = SeedUser(PasswordVerificationResult.Success);
        var (product, productUrl) = TestEntityFactory.CreateProduct("Doomed Product", user.Id);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.Alerts.Add(TestEntityFactory.CreateAlert(product.Id, user.Id, 50m));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var command = new DeleteAccount.Command("Password1") { UserId = user.Id };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        (await _dbContext.Products.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await _dbContext.ProductUrls.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await _dbContext.Alerts.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ThrowsUnauthorizedAndKeepsUser()
    {
        var user = SeedUser(PasswordVerificationResult.Failed);
        var command = new DeleteAccount.Command("WrongPassword1") { UserId = user.Id };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Password is incorrect");
        _dbContext.Users.Any(u => u.Id == user.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ThrowsUnauthorized()
    {
        var command = new DeleteAccount.Command("Password1") { UserId = Guid.NewGuid() };

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
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();

        _passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(It.IsAny<User>(), "hash", It.IsAny<string>()))
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
