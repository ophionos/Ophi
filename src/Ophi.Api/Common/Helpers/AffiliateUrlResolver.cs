using Microsoft.EntityFrameworkCore;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.Helpers;

/// <summary>
/// Loads a user's affiliate-tag map once and resolves affiliate URLs for stored product URLs.
/// Returns null from <see cref="Resolve"/> when affiliates are disabled, the URL has no
/// associated store, or the store has no affiliate configuration.
/// </summary>
public sealed class AffiliateUrlResolver
{
    private readonly Dictionary<string, (string ParamName, string Tag)>? _map;

    private AffiliateUrlResolver(Dictionary<string, (string, string)>? map) => _map = map;

    public static async Task<AffiliateUrlResolver> LoadAsync(
        OphiDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var enabled = await dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.AffiliatesEnabled)
            .FirstAsync(cancellationToken);

        if (!enabled)
            return new AffiliateUrlResolver(null);

        var map = await dbContext.StoreConfigurations
            .Where(s => s.UserId == userId && s.AffiliateParamName != null && s.AffiliateTag != null)
            .ToDictionaryAsync(
                s => s.StoreId,
                s => (s.AffiliateParamName!, s.AffiliateTag!),
                cancellationToken);

        return new AffiliateUrlResolver(map);
    }

    public string? Resolve(string url, string? storeId)
    {
        if (_map is null || storeId is null)
            return null;

        if (!_map.TryGetValue(storeId, out var aff))
            return null;

        return AffiliateUrlHelper.ApplyAffiliateCode(url, aff.ParamName, aff.Tag);
    }
}
