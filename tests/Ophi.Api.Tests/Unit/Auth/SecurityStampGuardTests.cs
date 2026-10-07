using System.Security.Claims;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Ophi.Api.Common.Auth;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Auth;

public class SecurityStampGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly MemoryCache _cache;
    private readonly SecurityStampGuard _guard;

    public SecurityStampGuardTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _guard = new SecurityStampGuard(_cache);
    }

    [Fact]
    public async Task ValidateAsync_WithMatchingStamp_ReturnsTrue()
    {
        var user = SeedUser();
        var principal = BuildPrincipal(user.Id, user.SecurityStamp);

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedStamp_ReturnsFalse()
    {
        var user = SeedUser();
        var principal = BuildPrincipal(user.Id, "stale-stamp-from-before-rotation");

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithMissingStampClaim_ReturnsFalse()
    {
        // Tickets issued before the stamp feature shipped carry no stamp claim — reject them.
        var user = SeedUser();
        var principal = BuildPrincipal(user.Id, stamp: null);

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithUnknownUser_ReturnsFalse()
    {
        var principal = BuildPrincipal(Guid.NewGuid(), "any-stamp");

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithNullPrincipal_ReturnsFalse()
    {
        var result = await _guard.ValidateAsync(null, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CachesStampAfterFirstLookup()
    {
        var user = SeedUser();
        var principal = BuildPrincipal(user.Id, user.SecurityStamp);
        await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        // Remove the user; a cached stamp means validation still succeeds within the TTL.
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_DoesNotCacheUnknownUsers()
    {
        var userId = Guid.NewGuid();
        var stamp = "fresh-stamp";
        var principal = BuildPrincipal(userId, stamp);
        await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        _dbContext.Users.Add(new User
        {
            Id = userId,
            Email = "late@example.com",
            Name = "Late User",
            PasswordHash = "hash",
            SecurityStamp = stamp
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_InvalidatesOldStampImmediately()
    {
        var user = SeedUser();
        var oldStamp = user.SecurityStamp;
        var principal = BuildPrincipal(user.Id, oldStamp);

        // Prime the cache with the old stamp, then rotate.
        await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);
        user.ChangePassword("new-hash");
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _guard.Refresh(user.Id, user.SecurityStamp);

        var oldSession = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);
        var newSession = await _guard.ValidateAsync(
            BuildPrincipal(user.Id, user.SecurityStamp), _dbContext, TestContext.Current.CancellationToken);

        oldSession.Should().BeFalse();
        newSession.Should().BeTrue();
    }

    [Fact]
    public async Task Evict_DropsCachedStampSoDeletionTakesEffectImmediately()
    {
        var user = SeedUser();
        var principal = BuildPrincipal(user.Id, user.SecurityStamp);
        await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _guard.Evict(user.Id);

        var result = await _guard.ValidateAsync(principal, _dbContext, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    private User SeedUser()
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
        return user;
    }

    private static ClaimsPrincipal BuildPrincipal(Guid userId, string? stamp)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (stamp is not null)
        {
            claims.Add(new Claim(SecurityStampGuard.ClaimType, stamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
