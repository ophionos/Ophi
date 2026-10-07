using Ophi.Infrastructure.Scraping.Adapters.StoreConfigs;

namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Provides store configurations from code (built-in stores only).
/// Does not support user-specific configurations - use CombinedStoreConfigProvider for that.
/// </summary>
public class CodeStoreConfigProvider : IStoreConfigProvider
{
    private readonly IReadOnlyList<StoreConfig> _configs =
    [
        AmazonConfig.Create(),
        EbayConfig.Create()
    ];

    private readonly StoreConfig _genericConfig = new()
    {
        Id = "generic",
        Name = "Generic Store",
        DomainPatterns = [],
        IsBuiltIn = true,
        Selectors = new StoreSelectorConfig
        {
            PriceSelectors = CommonSelectors.PriceSelectors,
            NameSelectors = CommonSelectors.NameSelectors,
            ImageSelectors = CommonSelectors.ImageSelectors,
            PriceRegexPatterns = CommonSelectors.PriceRegexPatterns,
            ImageRegexPatterns = CommonSelectors.ImageRegexPatterns
        }
    };

    public IReadOnlyList<StoreConfig> GetAllConfigs() => _configs;

    public StoreConfig? GetConfigForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();

        // Remove www. prefix for matching
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        return _configs.FirstOrDefault(config =>
            config.DomainPatterns.Select(pattern => pattern.ToLowerInvariant()).Any(lowerPattern =>
                host == lowerPattern || host.EndsWith($".{lowerPattern}", StringComparison.Ordinal)));
    }

    public StoreConfig GetGenericConfig() => _genericConfig;

    /// <summary>
    /// Returns built-in configs only (user-specific configs not supported by this provider).
    /// </summary>
    public Task<IReadOnlyList<StoreConfig>> GetConfigsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(GetAllConfigs());

    /// <summary>
    /// Returns built-in config match only (user-specific configs not supported by this provider).
    /// </summary>
    public Task<StoreConfig?> GetConfigForUrlAsync(string url, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(GetConfigForUrl(url));

    /// <summary>
    /// No-op for code-based provider (no cache to invalidate).
    /// </summary>
    public void InvalidateCache(Guid userId)
    {
        // No caching in code-based provider
    }
}
