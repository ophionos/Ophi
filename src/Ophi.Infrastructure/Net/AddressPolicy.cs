using System.Net;
using System.Net.Sockets;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// Which IP addresses an outbound request to a user-chosen URL may reach. Used by the API's literal-host
/// URL validation and by <see cref="PublicAddressHandler"/> on every resolved address at connect time.
/// </summary>
public static class AddressPolicy
{
    public static bool IsBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // An IPv6 address carrying an embedded IPv4 address ("[::ffff:169.254.169.254]", NAT64
        // "64:ff9b::a9fe:a9fe") reaches the same host as its dotted form, so unwrap it before the range
        // checks — otherwise every IPv4 rule below is bypassed by rewriting the address in v6 form.
        var ip = TryUnwrapIPv4(address) ?? address;
        var bytes = ip.GetAddressBytes();

        if (bytes.Length != 4)
        {
            return ip.Equals(IPAddress.IPv6Any)          // :: (unspecified)
                || ip.IsIPv6LinkLocal                    // fe80::/10
                || ip.IsIPv6SiteLocal                    // fec0::/10 (deprecated site-local)
                || ip.IsIPv6Multicast                    // ff00::/8
                || (bytes[0] & 0xFE) == 0xFC;            // fc00::/7 unique-local
        }

        return bytes[0] switch
        {
            0 => true,                                               // 0.0.0.0/8 — routes to localhost on Linux
            10 => true,                                              // 10.0.0.0/8
            127 => true,                                             // 127.0.0.0/8
            100 when bytes[1] >= 64 && bytes[1] <= 127 => true,      // 100.64.0.0/10 (CGNAT)
            169 when bytes[1] == 254 => true,                        // 169.254.0.0/16 (link-local + cloud metadata)
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,       // 172.16.0.0/12
            192 when bytes[1] == 0 && bytes[2] == 0 => true,         // 192.0.0.0/24 (IETF protocol assignments)
            192 when bytes[1] == 168 => true,                        // 192.168.0.0/16
            198 when bytes[1] is 18 or 19 => true,                   // 198.18.0.0/15 (benchmarking)
            >= 224 => true,                                          // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, broadcast
            _ => false
        };
    }

    /// <summary>
    /// The IPv4 address embedded in an IPv6 address: the mapped form (<c>::ffff:a.b.c.d</c>), the
    /// deprecated compatible form (<c>::a.b.c.d</c>, all-zero prefix) and the NAT64 well-known prefix
    /// (<c>64:ff9b::/96</c>). <c>::</c> and <c>::1</c> unwrap to <c>0.0.0.x</c>, which
    /// <c>0.0.0.0/8</c> blocks anyway.
    /// </summary>
    private static IPAddress? TryUnwrapIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return null;

        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        var isNat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B;
        for (var i = isNat64 ? 4 : 0; i < 12; i++)
        {
            if (bytes[i] != 0) return null;
        }

        return new IPAddress(bytes.AsSpan(12, 4));
    }
}
