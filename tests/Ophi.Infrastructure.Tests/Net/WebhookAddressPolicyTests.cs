using System.Net;
using FluentAssertions;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>
/// The operator allowlist re-opens private networks for webhooks only (a Gotify or ntfy on the LAN or
/// a tailnet). It can never re-open loopback, link-local (cloud metadata) or other reserved ranges.
/// </summary>
public class WebhookAddressPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FromConfiguration_Empty_BlocksExactlyWhatAddressPolicyBlocks(string? value)
    {
        var policy = WebhookAddressPolicy.FromConfiguration(value);

        policy.AllowedNetworks.Should().BeEmpty();
        policy.IsBlocked(IPAddress.Parse("192.168.1.10")).Should().BeTrue();
        policy.IsBlocked(IPAddress.Parse("100.100.1.1")).Should().BeTrue();
        policy.IsBlocked(IPAddress.Parse("93.184.215.14")).Should().BeFalse();
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("::ffff:192.168.1.10")] // the mapped IPv6 form reaches the same host
    [InlineData("100.101.102.103")]
    [InlineData("fd7a:115c:a1e0::1")]
    public void IsBlocked_AddressInAllowedNetwork_ReturnsFalse(string address)
    {
        var policy = WebhookAddressPolicy.FromConfiguration("192.168.1.0/24, 100.64.0.0/10, fd7a:115c:a1e0::/48");

        policy.IsBlocked(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("192.168.2.10")] // private, but outside the listed /24
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public void IsBlocked_BlockedAddressOutsideAllowedNetworks_ReturnsTrue(string address)
    {
        var policy = WebhookAddressPolicy.FromConfiguration("192.168.1.0/24");

        policy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.0/8")]
    [InlineData("169.254.0.0/16")]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/7")]       // wider than 10.0.0.0/8, so it takes in public space
    [InlineData("::/0")]
    [InlineData("::1/128")]
    [InlineData("93.184.215.0/24")] // public: already reachable, so listing it is a mistake
    [InlineData("not-a-network")]
    [InlineData("192.168.1.1/24")]  // host bits set
    public void FromConfiguration_EntryOutsideReopenableRanges_ThrowsNamingTheEntry(string entry)
    {
        var act = () => WebhookAddressPolicy.FromConfiguration($"192.168.1.0/24,{entry}");

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{entry}*");
    }

    [Fact]
    public void FromConfiguration_WholeReopenableRanges_AreAccepted()
    {
        var policy = WebhookAddressPolicy.FromConfiguration(
            "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,100.64.0.0/10,fc00::/7");

        policy.AllowedNetworks.Should().HaveCount(5);
    }
}
