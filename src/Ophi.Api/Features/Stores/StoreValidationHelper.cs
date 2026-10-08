using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Features.Stores;

internal static class StoreValidationHelper
{
    /// <summary>
    /// Checks that none of the requested domain patterns overlap with patterns already
    /// registered in any of the user's other stores. Pass <paramref name="excludeStoreId"/>
    /// when updating an existing store so its own domains are not flagged.
    /// </summary>
    public static async Task CheckDomainOverlapAsync(
        OphiDbContext dbContext,
        Guid userId,
        string[] requestedPatterns,
        Guid? excludeStoreId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.StoreConfigurations.Where(s => s.UserId == userId);
        if (excludeStoreId.HasValue)
            query = query.Where(s => s.Id != excludeStoreId.Value);

        var userStores = await query
            .Select(s => new { s.StoreId, s.DomainPatternsJson })
            .ToListAsync(cancellationToken);

        var requestDomains = requestedPatterns.Select(d => d.ToLowerInvariant()).ToHashSet();
        foreach (var store in userStores)
        {
            var storeDomains = JsonSerializer.Deserialize<string[]>(store.DomainPatternsJson) ?? [];
            var overlap = storeDomains.Where(d => requestDomains.Contains(d.ToLowerInvariant())).ToArray();
            if (overlap.Length > 0)
                throw new ApiException(
                    $"Domain pattern(s) '{string.Join("', '", overlap)}' already configured in store '{store.StoreId}'",
                    409, "Conflict");
        }
    }

    /// <summary>
    /// Validates that <paramref name="priceLocale"/> is a recognised .NET culture name.
    /// Throws <see cref="ApiException"/> (400) when invalid.
    /// </summary>
    public static void ValidatePriceLocale(string priceLocale)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(priceLocale);
        }
        catch (CultureNotFoundException)
        {
            throw new ApiException($"Invalid price locale: '{priceLocale}'", 400, "Bad Request");
        }
    }

    /// <summary>
    /// The one serialization of <c>DomainPatternsJson</c>. Validators measure this exact string
    /// against the column bound, so the handlers must store it — not a re-serialization.
    /// </summary>
    public static string SerializeDomainPatterns(string[] patterns) => JsonSerializer.Serialize(patterns);

    /// <summary>The one serialization of <c>SelectorsJson</c>; see <see cref="SerializeDomainPatterns"/>.</summary>
    public static string SerializeSelectors(StoreSelectorConfig config) => JsonSerializer.Serialize(config);

    public static bool DomainPatternsFitColumn(string[]? patterns) =>
        patterns is null ||
        SerializeDomainPatterns(patterns).Length <= StoreConfigurationConfiguration.DomainPatternsJsonMaxLength;

    public static bool SelectorsFitColumn(StoreSelectorConfig config) =>
        SerializeSelectors(config).Length <= StoreConfigurationConfiguration.SelectorsJsonMaxLength;

    public static readonly string DomainPatternsTooLongMessage =
        $"Domain patterns are too long (at most {StoreConfigurationConfiguration.DomainPatternsJsonMaxLength} characters in total)";

    public static readonly string SelectorsTooLongMessage =
        $"Selectors are too long (at most {StoreConfigurationConfiguration.SelectorsJsonMaxLength} characters in total)";

    public static readonly string PriceLocaleTooLongMessage =
        $"Price locale must not exceed {StoreConfigurationConfiguration.PriceLocaleMaxLength} characters";
}
