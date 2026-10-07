using FluentValidation;
using Ophi.Api.Common.Validators;
using Ophi.Infrastructure.Scraping;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class DetectStore
{
    public static void MapDetectStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/stores/detect", async (Request request, IMessageBus bus) =>
        {
            var command = new Command(request.Url);
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("DetectStore")
        .WithTags("Stores")
        .WithSummary("Detect store for a URL")
        .WithDescription("Checks if a matching store configuration exists for the given URL based on domain patterns. Returns the matched store or indicates no match was found. Checks both user-defined and built-in store configurations.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Request(string Url);

    public record Command(string Url);

    public record SelectorsDto(
        string[] PriceSelectors,
        string[] NameSelectors,
        string[] ImageSelectors,
        string[]? PriceRegexPatterns,
        string[]? PriceJsonPaths = null,
        string[]? NameJsonPaths = null,
        string[]? ImageJsonPaths = null
    );

    public record Response(
        bool Success,
        string? StoreName,
        string? Domain,
        SelectorsDto? Selectors,
        string? Error
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Url)
                .NotEmpty().WithMessage("URL is required")
                .MustBeValidHttpUrl("URL must be a valid public HTTP or HTTPS URL");
        }
    }

    public class Handler(IScrapingService scrapingService, IAutoCreateStoreService autoCreateStoreService, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            logger.LogInformation("Detecting store for URL {Url}", request.Url);
            var scrapeResult = await scrapingService.ScrapeProductAsync(request.Url, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(scrapeResult.FetchedHtml))
            {
                return new Response(false, null, null, null,
                    scrapeResult.Error ?? "Failed to fetch page content");
            }

            var analysisResult = await autoCreateStoreService.AnalyzeHtmlAsync(scrapeResult.FetchedHtml, request.Url, cancellationToken);

            if (analysisResult == null)
            {
                return new Response(false, null, null, null,
                    "Could not detect store selectors from the page");
            }

            return new Response(
                true,
                analysisResult.StoreName,
                analysisResult.Domain,
                new SelectorsDto(
                    analysisResult.Selectors.PriceSelectors,
                    analysisResult.Selectors.NameSelectors,
                    analysisResult.Selectors.ImageSelectors,
                    analysisResult.Selectors.PriceRegexPatterns,
                    analysisResult.Selectors.PriceJsonPaths,
                    analysisResult.Selectors.NameJsonPaths,
                    analysisResult.Selectors.ImageJsonPaths
                ),
                null
            );
        }
    }
}
