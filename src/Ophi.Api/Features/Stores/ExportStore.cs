using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class ExportStore
{
    public static void MapExportStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/stores/{id:guid}/export", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("ExportStore")
        .WithTags("Stores")
        .WithSummary("Export store configurations")
        .WithDescription("Exports a store configuration as a portable JSON object. The exported format can be shared with other users and imported via the import endpoint.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid Id, Guid UserId);

    public record Response(
        int Version,
        string StoreId,
        string Name,
        string[] DomainPatterns,
        SelectorDto Selectors,
        string PriceLocale,
        bool RequiresJavaScript,
        string? CurrencyOverride
    );

    public record SelectorDto(
        string[] PriceSelectors,
        string[] NameSelectors,
        string[] ImageSelectors,
        string[]? PriceRegexPatterns,
        string[]? ImageRegexPatterns,
        string[]? PriceJsonPaths = null,
        string[]? NameJsonPaths = null,
        string[]? ImageJsonPaths = null
    );

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Exporting store {StoreId} for user {UserId}", request.Id, request.UserId);

            var store = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.Id == request.Id && s.UserId == request.UserId, cancellationToken);

            if (store == null)
                throw new NotFoundException("Store not found");

            var domainPatterns = JsonSerializer.Deserialize<string[]>(store.DomainPatternsJson) ?? [];
            var selectors = JsonSerializer.Deserialize<StoreSelectorConfig>(store.SelectorsJson)
                ?? new StoreSelectorConfig { PriceSelectors = [], NameSelectors = [], ImageSelectors = [] };

            return new Response(
                1,
                store.StoreId,
                store.Name,
                domainPatterns,
                new SelectorDto(
                    selectors.PriceSelectors,
                    selectors.NameSelectors,
                    selectors.ImageSelectors,
                    selectors.PriceRegexPatterns,
                    selectors.ImageRegexPatterns,
                    selectors.PriceJsonPaths,
                    selectors.NameJsonPaths,
                    selectors.ImageJsonPaths
                ),
                store.PriceLocale,
                store.RequiresJavaScript,
                store.CurrencyOverride
            );
        }
    }
}
