using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.ApiKeys;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;

namespace Ophi.Api.Tests.Unit.ApiKeys;

public class CreateApiKeyTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Ophi.Infrastructure.Persistence.OphiDbContext _dbContext;
    private readonly Guid _userId;
    private readonly CreateApiKey.Validator _validator = new();

    public CreateApiKeyTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _userId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _userId,
            Email = "test@example.com",
            PasswordHash = "hash",
            Name = "Test"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_CreatesApiKeyAndReturnsRawKey()
    {
        var handler = new CreateApiKey.Handler(_dbContext, NullLogger<CreateApiKey.Handler>.Instance);
        var command = new CreateApiKey.Command("My Script", ["read"], null) { UserId = _userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("My Script");
        result.Scopes.Should().Contain("read");
        result.Key.Should().StartWith("ophi_");
        result.Key.Length.Should().BeGreaterThan(10);

        // Key should be stored as hash, not raw
        var stored = _dbContext.ApiKeys.First();
        stored.KeyHash.Should().NotBe(result.Key);
        stored.KeyHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task Handle_WithExpiresAt_SetsExpiration()
    {
        var handler = new CreateApiKey.Handler(_dbContext, NullLogger<CreateApiKey.Handler>.Instance);
        var expiresAt = DateTime.UtcNow.AddDays(30);
        var command = new CreateApiKey.Command("Expiring Key", ["read", "write"], expiresAt) { UserId = _userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task Handle_DeduplicatesScopes()
    {
        var handler = new CreateApiKey.Handler(_dbContext, NullLogger<CreateApiKey.Handler>.Instance);
        var command = new CreateApiKey.Command("Duped Scopes", ["read", "read", "write"], null) { UserId = _userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Scopes.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var command = new CreateApiKey.Command("", ["read"], null) { UserId = _userId };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_EmptyScopes_Fails()
    {
        var command = new CreateApiKey.Command("Test", [], null) { UserId = _userId };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Scopes);
    }

    [Fact]
    public void Validate_InvalidScope_Fails()
    {
        var command = new CreateApiKey.Command("Test", ["admin"], null) { UserId = _userId };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Scopes[0]");
    }

    [Fact]
    public void Validate_PastExpiresAt_Fails()
    {
        var command = new CreateApiKey.Command("Test", ["read"], DateTime.UtcNow.AddDays(-1)) { UserId = _userId };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ExpiresAt);
    }

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var command = new CreateApiKey.Command("My Key", ["read", "write"], DateTime.UtcNow.AddDays(30)) { UserId = _userId };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
