using FluentAssertions;
using Ophi.Domain.Entities;

namespace Ophi.Domain.Tests;

public class StoreClearanceTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithMixedCaseHost_StoresTheHostInLowerCase()
    {
        var clearance = StoreClearance.Create(Guid.NewGuid(), "WWW.Shop.Example", "{}", "UA", Now);

        clearance.Host.Should().Be("www.shop.example");
    }

    [Fact]
    public void Create_Always_ExpiresAfterTheLifetime()
    {
        var clearance = StoreClearance.Create(Guid.NewGuid(), "shop.example", "{}", "UA", Now);

        clearance.ExpiresAt.Should().Be(Now + StoreClearance.Lifetime);
        clearance.IsValidAt(Now + StoreClearance.Lifetime - TimeSpan.FromSeconds(1)).Should().BeTrue();
        clearance.IsValidAt(Now + StoreClearance.Lifetime).Should().BeFalse();
    }

    [Fact]
    public void Create_Always_KeepsTheStateAndUserAgent()
    {
        var userId = Guid.NewGuid();

        var clearance = StoreClearance.Create(userId, "shop.example", "{\"cookies\":[]}", "Mozilla/5.0", Now);

        clearance.UserId.Should().Be(userId);
        clearance.StorageState.Should().Be("{\"cookies\":[]}");
        clearance.UserAgent.Should().Be("Mozilla/5.0");
        clearance.Id.Should().NotBeEmpty();
    }
}
