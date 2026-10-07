using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Provides store configurations by combining built-in (code) and user-defined (database) configs.
/// User-defined configs take precedence over built-in configs for matching domains.
/// </summary>
public class CombinedStoreConfigProvider(OphiDbContext dbContext, CodeStoreConfigProvider codeProvider, IMemoryCache cache) : IStoreConfigProvider
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "store-configs-";

    /// <summary>
    /// Gets all built-in store configurations (synchronous, for backwards compatibility).
    /// For user-specific configs, use GetConfigsForUserAsync.
    /// </summary>
    public IReadOnlyList<StoreConfig> GetAllConfigs() => codeProvider.GetAllConfigs();

    /// <summary>
    /// Gets the built-in store configuration that matches the given URL (synchronous).
    /// For user-specific matching, use GetConfigForUrlAsync.
    /// </summary>
    public StoreConfig? GetConfigForUrl(string url) => codeProvider.GetConfigForUrl(url);

    /// <summary>
    /// Gets the generic/fallback store configuration.
    /// </summary>
    public StoreConfig GetGenericConfig() => codeProvider.GetGenericConfig();

    /// <summary>
    /// Gets all store configurations for a specific user (includes built-in stores).
    /// User-defined configs are listed first.
    /// </summary>
    public async Task<IReadOnlyList<StoreConfig>> GetConfigsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userConfigs = await GetUserConfigsFromCacheOrDbAsync(userId, cancellationToken);
        var builtInConfigs = codeProvider.GetAllConfigs();

        // Combine: user configs first, then built-in configs that don't conflict
        var result = new List<StoreConfig>(userConfigs);
        var userStoreIds = new HashSet<string>(userConfigs.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        result.AddRange(builtInConfigs.Where(builtIn => !userStoreIds.Contains(builtIn.Id)));

        return result;
    }

    /// <summary>
    /// Gets the store configuration that matches the given URL for a specific user.
    /// User-defined configs take precedence over built-in configs.
    /// </summary>
    public async Task<StoreConfig?> GetConfigForUrlAsync(string url, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = NormalizeHost(uri.Host);

        // First, check user-defined configs
        var userConfigs = await GetUserConfigsFromCacheOrDbAsync(userId, cancellationToken);
        var userMatch = FindMatchingConfig(userConfigs, host);
        
        // Fall back to built-in configs
        return userMatch ?? codeProvider.GetConfigForUrl(url);
    }

    /// <summary>
    /// Invalidates the cache for a specific user's store configurations.
    /// </summary>
    public void InvalidateCache(Guid userId)
    {
        var cacheKey = $"{CacheKeyPrefix}{userId}";
        cache.Remove(cacheKey);
    }

    private async Task<IReadOnlyList<StoreConfig>> GetUserConfigsFromCacheOrDbAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeyPrefix}{userId}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<StoreConfig>? cached) && cached != null)
        {
            return cached;
        }

        var dbConfigs = await dbContext.StoreConfigurations
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

        var configs = dbConfigs.Select(ToStoreConfig).ToList();

        cache.Set<IReadOnlyList<StoreConfig>>(cacheKey, configs, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheExpiration
        });

        return configs;
    }

    private static StoreConfig ToStoreConfig(Domain.Entities.StoreConfiguration entity)
    {
        var domainPatterns = JsonSerializer.Deserialize<string[]>(entity.DomainPatternsJson) ?? [];
        var selectors = JsonSerializer.Deserialize<StoreSelectorConfig>(entity.SelectorsJson)
            ?? new StoreSelectorConfig
            {
                PriceSelectors = [],
                NameSelectors = [],
                ImageSelectors = []
            };

        return new StoreConfig
        {
            Id = entity.StoreId,
            Name = entity.Name,
            DomainPatterns = domainPatterns,
            Selectors = selectors,
            IsBuiltIn = false,
            EntityId = entity.Id,
            CreatedAt = entity.CreatedAt,
            PriceLocale = entity.PriceLocale,
            RequiresJavaScript = entity.RequiresJavaScript,
            IsAutoCreated = entity.IsAutoCreated,
            CurrencyOverride = entity.CurrencyOverride,
            AffiliateParamName = entity.AffiliateParamName,
            AffiliateTag = entity.AffiliateTag,
            CustomUserAgent = entity.CustomUserAgent
        };
    }

    private static string NormalizeHost(string host) => ScrapeHelpers.NormalizeHost(host);

    private static StoreConfig? FindMatchingConfig(IReadOnlyList<StoreConfig> configs, string host) =>
        configs.FirstOrDefault(config =>
            config.DomainPatterns.Select(pattern => pattern.ToLowerInvariant()).Any(lowerPattern =>
                host == lowerPattern || host.EndsWith($".{lowerPattern}", StringComparison.Ordinal)));
}
