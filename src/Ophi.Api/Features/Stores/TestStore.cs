using FluentValidation;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class TestStore
{
    public static void MapTestStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/stores/test", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.StoreId, request.TestUrl)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("TestStore")
        .WithTags("Stores")
        .WithSummary("Test a store configuration")
        .WithDescription("Performs a live scrape using the provided store configuration and URL without saving anything. Returns the extracted product name, price, currency, and image URL. Useful for validating selectors before saving a store configuration.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Request(string StoreId, string TestUrl);

    public record Command(string StoreId, string TestUrl)
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        bool Success,
        string? ExtractedName,
        decimal? ExtractedPrice,
        string? ExtractedImageUrl,
        string? Currency,
        string? DetectedSelector,
        string? Error
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StoreId)
                .NotEmpty().WithMessage("Store ID is required");

            RuleFor(x => x.TestUrl)
                .NotEmpty().WithMessage("Test URL is required")
                .MustBeValidHttpUrl("Test URL must be a valid HTTP or HTTPS URL");
        }
    }

    public class Handler(IStoreConfigProvider configProvider, IScrapingService scrapingService, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var configs = await configProvider.GetConfigsForUserAsync(request.UserId, cancellationToken);
            var config = configs.FirstOrDefault(c => c.Id.Equals(request.StoreId, StringComparison.OrdinalIgnoreCase));

            if (config == null)
            {
                throw new NotFoundException($"Store '{request.StoreId}' not found");
            }

            logger.LogInformation("Testing store scrape for URL {Url}", request.TestUrl);

            var result = await scrapingService.ScrapeWithConfigAsync(request.TestUrl, config, cancellationToken);

            return new Response(
                result.Success,
                result.Name,
                result.Price,
                result.ImageUrl,
                result.Currency,
                result.DetectedSelector,
                result.Error
            );
        }
    }
}
