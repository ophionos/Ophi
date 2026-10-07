using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Stores;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class SetStoreAffiliateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly SetStoreAffiliate.Handler _handler;
    private readonly Guid _testUserId;

    public SetStoreAffiliateTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        var configProvider = new CodeStoreConfigProvider();
        _handler = new SetStoreAffiliate.Handler(_dbContext, configProvider, NullLogger<SetStoreAffiliate.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    // --- Handler Tests ---

    [Fact]
    public async Task Handle_WithExistingStore_UpdatesAffiliateFields()
    {
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            StoreId = "my-store",
            Name = "My Store",
            UserId = _testUserId
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SetStoreAffiliate.Command("my-store", "ref", "abc123") { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.StoreId.Should().Be("my-store");
        result.AffiliateParamName.Should().Be("ref");
        result.AffiliateTag.Should().Be("abc123");

        var stored = _dbContext.StoreConfigurations.First(s => s.StoreId == "my-store" && s.UserId == _testUserId);
        stored.AffiliateParamName.Should().Be("ref");
        stored.AffiliateTag.Should().Be("abc123");
    }

    [Fact]
    public async Task Handle_WithBuiltInStore_CreatesSkeletonConfig()
    {
        var command = new SetStoreAffiliate.Command("amazon", "tag", "ophi-20") { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.StoreId.Should().Be("amazon");
        result.AffiliateParamName.Should().Be("tag");
        result.AffiliateTag.Should().Be("ophi-20");

        var stored = _dbContext.StoreConfigurations.First(s => s.StoreId == "amazon" && s.UserId == _testUserId);
        stored.AffiliateParamName.Should().Be("tag");
        stored.AffiliateTag.Should().Be("ophi-20");
        stored.DomainPatternsJson.Should().Be("[]");
        stored.IsAutoCreated.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNullFields_ClearsAffiliate()
    {
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            StoreId = "my-store",
            Name = "My Store",
            UserId = _testUserId,
            AffiliateParamName = "ref",
            AffiliateTag = "old-code"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SetStoreAffiliate.Command("my-store", null, null) { UserId = _testUserId };
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.AffiliateParamName.Should().BeNull();
        result.AffiliateTag.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithUnknownStoreId_ThrowsNotFound()
    {
        var command = new SetStoreAffiliate.Command("nonexistent-store", "ref", "abc") { UserId = _testUserId };

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // --- Validator Tests ---

    [Fact]
    public void Validate_WithValidData_Passes()
    {
        var validator = new SetStoreAffiliate.Validator();
        var command = new SetStoreAffiliate.Command("amazon", "tag", "ophi-20") { UserId = _testUserId };

        var result = validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithWhitespaceParamName_Fails()
    {
        var validator = new SetStoreAffiliate.Validator();
        var command = new SetStoreAffiliate.Command("amazon", "my tag", "ophi-20") { UserId = _testUserId };

        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AffiliateParamName);
    }

    [Fact]
    public void Validate_WithTooLongParamName_Fails()
    {
        var validator = new SetStoreAffiliate.Validator();
        var command = new SetStoreAffiliate.Command("amazon", new string('a', 51), "ophi-20") { UserId = _testUserId };

        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AffiliateParamName);
    }

    [Fact]
    public void Validate_WithTooLongTag_Fails()
    {
        var validator = new SetStoreAffiliate.Validator();
        var command = new SetStoreAffiliate.Command("amazon", "tag", new string('a', 101)) { UserId = _testUserId };

        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AffiliateTag);
    }

    [Fact]
    public void Validate_WithNullParams_Passes()
    {
        var validator = new SetStoreAffiliate.Validator();
        var command = new SetStoreAffiliate.Command("amazon", null, null) { UserId = _testUserId };

        var result = validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
