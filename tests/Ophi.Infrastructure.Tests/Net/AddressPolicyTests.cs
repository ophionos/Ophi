using System.Net;
using FluentAssertions;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Tests.Net;

public class AddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("239.255.255.250")]    // multicast (SSDP)
    [InlineData("240.0.0.1")]          // reserved
    [InlineData("255.255.255.255")]    // broadcast
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]            // IPv6 multicast
    [InlineData("::ffff:10.0.0.1")]    // IPv4-mapped
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 of 169.254.169.254
    public void IsBlocked_PrivateOrReservedAddress_ReturnsTrue(string address)
    {
        AddressPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.215.14")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("64:ff9b::808:808")]   // NAT64 of 8.8.8.8
    public void IsBlocked_PublicAddress_ReturnsFalse(string address)
    {
        AddressPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeFalse();
    }
}
