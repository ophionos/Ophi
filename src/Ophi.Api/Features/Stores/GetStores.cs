using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class GetStores
{
    public static void MapGetStoresEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/stores", async (IMessageBus bus, HttpContext context, [Microsoft.AspNetCore.Mvc.FromQuery] string? search = null) =>
        {
            var query = new Query(context.User.GetUserId(), search);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetStores")
        .WithTags("Stores")
        .WithSummary("List store configurations")
        .WithDescription("Returns all store configurations for the current user including selector rules and domain patterns. Supports optional search filtering by name or domain.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId, string? Search = null);

    public record Response(List<StoreDto> Items, int Total);

    public record StoreDto(
        Guid? Id,
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        bool IsBuiltIn,
        bool IsAutoCreated,
        DateTime? CreatedAt,
        string PriceLocale,
        bool RequiresJavaScript,
        string? CurrencyOverride,
        string? AffiliateParamName,
        string? AffiliateTag,
        string? CustomUserAgent
    );

    public record StoreSelectorDto(
        string[] PriceSelectors,
        string[] NameSelectors,
        string[] ImageSelectors,
        string[]? PriceRegexPatterns,
        string[]? ImageRegexPatterns,
        string[]? PriceJsonPaths = null,
        string[]? NameJsonPaths = null,
        string[]? ImageJsonPaths = null
    );

    public class Handler(IStoreConfigProvider configProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching store configurations for user {UserId}", request.UserId);

            var configs = await configProvider.GetConfigsForUserAsync(request.UserId, cancellationToken);

            var stores = configs
                .Where(c => request.Search == null || c.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                .Select(c => new StoreDto(
                c.EntityId,
                c.Id,
                c.Name,
                c.DomainPatterns,
                new StoreSelectorDto(
                    c.Selectors.PriceSelectors,
                    c.Selectors.NameSelectors,
                    c.Selectors.ImageSelectors,
                    c.Selectors.PriceRegexPatterns,
                    c.Selectors.ImageRegexPatterns,
                    c.Selectors.PriceJsonPaths,
                    c.Selectors.NameJsonPaths,
                    c.Selectors.ImageJsonPaths
                ),
                c.IsBuiltIn,
                c.IsAutoCreated,
                c.CreatedAt,
                c.PriceLocale,
                c.RequiresJavaScript,
                c.CurrencyOverride,
                c.AffiliateParamName,
                c.AffiliateTag,
                c.CustomUserAgent
            )).ToList();

            logger.LogDebug("Returning {Count} store configurations for user {UserId}", stores.Count, request.UserId);
            return new Response(stores, stores.Count);
        }
    }
}
