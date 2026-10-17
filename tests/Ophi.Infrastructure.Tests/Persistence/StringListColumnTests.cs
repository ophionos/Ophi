using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>ApiKey.Scopes</c> and <c>WebhookTarget.Events</c> are <c>List&lt;string&gt;</c> stored as one
/// comma-joined column. Without a value comparer EF compares the list by reference, so an in-place
/// mutation was never detected and silently not saved (#66).
/// </summary>
public class StringListColumnTests : IDisposable
{
    private readonly OphiDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public StringListColumnTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
    }

    [Fact]
    public async Task SaveChanges_ApiKeyScopesMutatedInPlace_PersistsTheChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = TestEntityFactory.User().Build();
        var key = new ApiKey { UserId = user.Id, Name = "key", KeyHash = new string('a', 64), Scopes = ["read"] };
        _dbContext.Users.Add(user);
        _dbContext.ApiKeys.Add(key);
        await _dbContext.SaveChangesAsync(ct);

        key.Scopes.Add("write");
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.ApiKeys.SingleAsync(k => k.Id == key.Id, ct);
        reloaded.Scopes.Should().Equal("read", "write");
    }

    [Fact]
    public async Task SaveChanges_WebhookEventsMutatedInPlace_PersistsTheChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = TestEntityFactory.User().Build();
        var target = new WebhookTarget
        {
            UserId = user.Id, Name = "hook", Url = "https://example.com/hook", Events = ["alert_fired"]
        };
        _dbContext.Users.Add(user);
        _dbContext.WebhookTargets.Add(target);
        await _dbContext.SaveChangesAsync(ct);

        target.Events.Remove("alert_fired");
        target.Events.Add("price_drop");
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.WebhookTargets.SingleAsync(w => w.Id == target.Id, ct);
        reloaded.Events.Should().Equal("price_drop");
    }

    [Fact]
    public async Task SaveChanges_ListUnchanged_DoesNotMarkThePropertyModified()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = TestEntityFactory.User().Build();
        var key = new ApiKey { UserId = user.Id, Name = "key", KeyHash = new string('b', 64), Scopes = ["read"] };
        _dbContext.Users.Add(user);
        _dbContext.ApiKeys.Add(key);
        await _dbContext.SaveChangesAsync(ct);

        key.Scopes = ["read"];
        _dbContext.ChangeTracker.DetectChanges();

        _dbContext.Entry(key).Property(k => k.Scopes).IsModified.Should().BeFalse();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
