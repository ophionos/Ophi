using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Tests.Helpers;

namespace Ophi.Infrastructure.Tests.Scraping;

public class StoreClearanceStoreTests : HandlerTestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly StoreClearanceStore _store;

    public StoreClearanceStoreTests()
    {
        _store = new StoreClearanceStore(DbContext, _time);
    }

    [Fact]
    public async Task Find_AfterSave_ReturnsTheClearanceForAnyHostCase()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TestUserId, "www.shop.example", "{\"cookies\":[]}", "UA", ct);

        var found = await _store.FindAsync(TestUserId, "WWW.Shop.Example", ct);

        found.Should().NotBeNull();
        found!.StorageState.Should().Be("{\"cookies\":[]}");
        found.UserAgent.Should().Be("UA");
    }

    [Fact]
    public async Task Save_SecondTimeForTheSameHost_ReplacesTheFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TestUserId, "shop.example", "old", "UA1", ct);

        await _store.SaveAsync(TestUserId, "shop.example", "new", "UA2", ct);

        (await DbContext.StoreClearances.CountAsync(ct)).Should().Be(1);
        (await _store.FindAsync(TestUserId, "shop.example", ct))!.StorageState.Should().Be("new");
    }

    [Fact]
    public async Task Find_WhenExpired_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TestUserId, "shop.example", "{}", "UA", ct);

        _time.Advance(StoreClearance.Lifetime);

        (await _store.FindAsync(TestUserId, "shop.example", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Find_ForAnotherUser_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TestUserId, "shop.example", "{}", "UA", ct);

        (await _store.FindAsync(Guid.NewGuid(), "shop.example", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheClearance()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TestUserId, "shop.example", "{}", "UA", ct);

        await _store.DeleteAsync(TestUserId, "Shop.Example", ct);

        (await DbContext.StoreClearances.CountAsync(ct)).Should().Be(0);
    }
}
