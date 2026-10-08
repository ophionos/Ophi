using System.Net;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// <see cref="AddressPolicy"/> for outbound webhooks, plus the networks the operator re-opened with
/// <c>Webhooks:AllowedNetworks</c> so a self-hosted Gotify, ntfy, Apprise or Home Assistant on the LAN
/// or a tailnet is reachable. Applies to the webhook client and the webhook URL validation only — the
/// scraper keeps <see cref="AddressPolicy"/>. Every account on the instance can target the listed
/// networks, so it is a single-operator setting.
/// </summary>
public sealed class WebhookAddressPolicy
{
    public const string ConfigKey = "Webhooks:AllowedNetworks";

    private WebhookAddressPolicy(IReadOnlyList<IPNetwork> allowedNetworks) => AllowedNetworks = allowedNetworks;

    public IReadOnlyList<IPNetwork> AllowedNetworks { get; }

    /// <summary>
    /// Parses a comma-separated CIDR list. Throws at startup when an entry is malformed or not fully
    /// inside <see cref="AddressPolicy.ReopenableNetworks"/>, so a typo cannot open loopback or the
    /// cloud metadata range.
    /// </summary>
    public static WebhookAddressPolicy FromConfiguration(string? value)
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // TryParse accepts host bits ("192.168.1.1/24"); refuse them, since the operator then meant
            // something other than the network that would be opened.
            if (!IPNetwork.TryParse(entry, out var network) || HasHostBits(entry, network.PrefixLength))
                throw new InvalidOperationException(
                    $"{ConfigKey}: '{entry}' is not a CIDR network (for example 192.168.1.0/24; host bits must be zero).");

            if (!AddressPolicy.ReopenableNetworks.Any(range =>
                    range.Contains(network.BaseAddress) && network.PrefixLength >= range.PrefixLength))
                throw new InvalidOperationException(
                    $"{ConfigKey}: '{entry}' is outside the networks that can be re-opened " +
                    $"({string.Join(", ", AddressPolicy.ReopenableNetworks)}).");

            networks.Add(network);
        }

        return new WebhookAddressPolicy(networks);
    }

    private static bool HasHostBits(string entry, int prefixLength)
    {
        var bytes = IPAddress.Parse(entry.Split('/')[0]).GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var networkBits = Math.Clamp(prefixLength - i * 8, 0, 8);
            var hostMask = 0xFF >> networkBits;
            if ((bytes[i] & hostMask) != 0)
                return true;
        }

        return false;
    }

    public bool IsBlocked(IPAddress address)
    {
        if (!AddressPolicy.IsBlocked(address))
            return false;

        // Unwrap first, as AddressPolicy does: "::ffff:192.168.1.10" must match 192.168.1.0/24.
        var ip = AddressPolicy.TryUnwrapIPv4(address) ?? address;
        return !AllowedNetworks.Any(network => network.Contains(ip));
    }
}
