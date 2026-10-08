using FluentValidation;
using Json.Path;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class CreateStore
{
    public static void MapCreateStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/stores", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(
                request.StoreId,
                request.Name,
                request.DomainPatterns,
                request.Selectors,
                request.PriceLocale,
                request.RequiresJavaScript,
                request.CurrencyOverride,
                request.AffiliateParamName,
                request.AffiliateTag,
                request.CustomUserAgent
            )
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/stores/{result.Id}", result);
        })
        .WithName("CreateStore")
        .WithTags("Stores")
        .WithSummary("Create a store configuration")
        .WithDescription("Creates a custom store scraping configuration with CSS selectors for price, name, image, and currency extraction. Specify a domain pattern to auto-match URLs. Set requiresJavaScript to true for sites that need browser-based scraping.")
        .Produces<Response>(201)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.StoreCreation);
    }

    public record Request(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        string? PriceLocale = "en-US",
        bool? RequiresJavaScript = false,
        string? CurrencyOverride = null,
        string? AffiliateParamName = null,
        string? AffiliateTag = null,
        string? CustomUserAgent = null
    );

    public record Command(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        string? PriceLocale = "en-US",
        bool? RequiresJavaScript = false,
        string? CurrencyOverride = null,
        string? AffiliateParamName = null,
        string? AffiliateTag = null,
        string? CustomUserAgent = null
    )
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        bool IsBuiltIn,
        bool IsAutoCreated,
        DateTime CreatedAt,
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
    )
    {
        public StoreSelectorConfig ToConfig() => new()
        {
            PriceSelectors = PriceSelectors,
            NameSelectors = NameSelectors,
            ImageSelectors = ImageSelectors,
            PriceRegexPatterns = PriceRegexPatterns,
            ImageRegexPatterns = ImageRegexPatterns,
            PriceJsonPaths = PriceJsonPaths,
            NameJsonPaths = NameJsonPaths,
            ImageJsonPaths = ImageJsonPaths
        };
    }

    public class Validator : AbstractValidator<Command>
    {
        private static readonly string[] BuiltInStoreIds = ["amazon", "ebay", "generic"];

        public Validator()
        {
            RuleFor(x => x.StoreId)
                .NotEmpty().WithMessage("Store ID is required")
                .MinimumLength(3).WithMessage("Store ID must be at least 3 characters")
                .MaximumLength(50).WithMessage("Store ID must not exceed 50 characters")
                .Matches(@"^[a-z0-9\-]+$").WithMessage("Store ID must contain only lowercase letters, numbers, and hyphens")
                .Must(id => !BuiltInStoreIds.Contains(id.ToLowerInvariant()))
                .WithMessage("Cannot use a built-in store ID");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required")
                .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

            RuleFor(x => x.DomainPatterns)
                .NotEmpty().WithMessage("At least one domain pattern is required")
                .Must(patterns => patterns.All(p => !string.IsNullOrWhiteSpace(p)))
                .WithMessage("Domain patterns cannot be empty");

            RuleFor(x => x.DomainPatterns)
                .Must(StoreValidationHelper.DomainPatternsFitColumn)
                .WithMessage(StoreValidationHelper.DomainPatternsTooLongMessage);

            RuleFor(x => x.Selectors)
                .Must(s => StoreValidationHelper.SelectorsFitColumn(s.ToConfig()))
                .When(x => x.Selectors != null)
                .WithMessage(StoreValidationHelper.SelectorsTooLongMessage);

            RuleFor(x => x.PriceLocale)
                .MaximumLength(StoreConfigurationConfiguration.PriceLocaleMaxLength)
                .WithMessage(StoreValidationHelper.PriceLocaleTooLongMessage);

            RuleFor(x => x.Selectors)
                .NotNull().WithMessage("Selectors are required")
                .DependentRules(() =>
                {
                    RuleFor(x => x.Selectors.PriceSelectors)
                        .NotEmpty().WithMessage("At least one price selector is required")
                        .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
                        .WithMessage("Price selectors cannot be empty or whitespace");

                    RuleFor(x => x.Selectors.NameSelectors)
                        .NotEmpty().WithMessage("At least one name selector is required")
                        .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
                        .WithMessage("Name selectors cannot be empty or whitespace");

                    RuleFor(x => x.Selectors.ImageSelectors)
                        .NotEmpty().WithMessage("At least one image selector is required")
                        .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
                        .WithMessage("Image selectors cannot be empty or whitespace");

                    RuleForEach(x => x.Selectors.PriceJsonPaths)
                        .Must(p => !string.IsNullOrWhiteSpace(p) && JsonPath.TryParse(p, out _))
                        .WithMessage("Invalid JSONPath expression")
                        .When(x => x.Selectors.PriceJsonPaths is { Length: > 0 });

                    RuleForEach(x => x.Selectors.NameJsonPaths)
                        .Must(p => !string.IsNullOrWhiteSpace(p) && JsonPath.TryParse(p, out _))
                        .WithMessage("Invalid JSONPath expression")
                        .When(x => x.Selectors.NameJsonPaths is { Length: > 0 });

                    RuleForEach(x => x.Selectors.ImageJsonPaths)
                        .Must(p => !string.IsNullOrWhiteSpace(p) && JsonPath.TryParse(p, out _))
                        .WithMessage("Invalid JSONPath expression")
                        .When(x => x.Selectors.ImageJsonPaths is { Length: > 0 });
                });

            RuleFor(x => x.CurrencyOverride)
                .Matches(@"^[A-Z]{3}$")
                .When(x => x.CurrencyOverride != null)
                .WithMessage("Currency override must be a 3-letter uppercase ISO 4217 code (e.g., EUR, USD)");
        }
    }

    public class Handler(OphiDbContext dbContext, IStoreConfigProvider configProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var existingStore = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.StoreId == request.StoreId, cancellationToken);

            if (existingStore != null)
            {
                throw new ApiException("A store with this ID already exists", 409, "Conflict");
            }

            // Check for duplicate domain patterns across user's stores
            await StoreValidationHelper.CheckDomainOverlapAsync(dbContext, request.UserId, request.DomainPatterns, null, cancellationToken);

            // Validate PriceLocale
            var priceLocale = request.PriceLocale ?? "en-US";
            StoreValidationHelper.ValidatePriceLocale(priceLocale);

            var selectorConfig = request.Selectors.ToConfig();

            var storeConfiguration = new StoreConfiguration
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                StoreId = request.StoreId,
                Name = request.Name,
                DomainPatternsJson = StoreValidationHelper.SerializeDomainPatterns(request.DomainPatterns),
                SelectorsJson = StoreValidationHelper.SerializeSelectors(selectorConfig),
                PriceLocale = priceLocale,
                RequiresJavaScript = request.RequiresJavaScript ?? false,
                CurrencyOverride = request.CurrencyOverride,
                AffiliateParamName = request.AffiliateParamName,
                AffiliateTag = request.AffiliateTag,
                CustomUserAgent = request.CustomUserAgent
            };

            dbContext.StoreConfigurations.Add(storeConfiguration);
            await dbContext.SaveChangesAsync(cancellationToken);

            configProvider.InvalidateCache(request.UserId);

            logger.LogInformation("Store {StoreId} created for user {UserId}", storeConfiguration.StoreId, request.UserId);

            return new Response(
                storeConfiguration.Id,
                storeConfiguration.StoreId,
                storeConfiguration.Name,
                request.DomainPatterns,
                request.Selectors,
                false,
                false,
                storeConfiguration.CreatedAt,
                storeConfiguration.PriceLocale,
                storeConfiguration.RequiresJavaScript,
                storeConfiguration.CurrencyOverride,
                storeConfiguration.AffiliateParamName,
                storeConfiguration.AffiliateTag,
                storeConfiguration.CustomUserAgent
            );
        }
    }
}
