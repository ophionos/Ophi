namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Provides store configurations for URL matching and scraping.
/// Abstraction allows swapping between code-based and database-backed implementations.
/// </summary>
public interface IStoreConfigProvider
{
    /// <summary>
    /// Gets all registered store configurations (built-in and user-defined).
    /// </summary>
    IReadOnlyList<StoreConfig> GetAllConfigs();

    /// <summary>
    /// Gets the store configuration that matches the given URL, or null if no specific store matches.
    /// </summary>
    /// <param name="url">The URL to match against store domain patterns.</param>
    /// <returns>The matching store config, or null if no specific store matches.</returns>
    StoreConfig? GetConfigForUrl(string url);

    /// <summary>
    /// Gets the generic/fallback store configuration used when no specific store matches.
    /// </summary>
    StoreConfig GetGenericConfig();

    /// <summary>
    /// Gets all store configurations for a specific user (includes built-in stores).
    /// </summary>
    /// <param name="userId">The user ID to get configurations for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of store configurations available to the user.</returns>
    Task<IReadOnlyList<StoreConfig>> GetConfigsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the store configuration that matches the given URL for a specific user.
    /// User-defined configs take precedence over built-in configs.
    /// </summary>
    /// <param name="url">The URL to match against store domain patterns.</param>
    /// <param name="userId">The user ID for user-specific matching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching store config, or null if no specific store matches.</returns>
    Task<StoreConfig?> GetConfigForUrlAsync(string url, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cache for a specific user's store configurations.
    /// Call this when a user's store configuration is created, updated, or deleted.
    /// </summary>
    /// <param name="userId">The user ID whose cache should be invalidated.</param>
    void InvalidateCache(Guid userId);
}
