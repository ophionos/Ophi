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

public static class ImportStore
{
    public static void MapImportStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/stores/import", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(
                request.StoreId,
                request.Name,
                request.DomainPatterns,
                request.Selectors,
                request.PriceLocale,
                request.RequiresJavaScript,
                request.CurrencyOverride
            )
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/stores/{result.Id}", result);
        })
        .WithName("ImportStore")
        .WithTags("Stores")
        .WithSummary("Import store configurations")
        .WithDescription("Imports a store configuration from JSON (exported from another instance or user). If a store with the same domain already exists, it is updated instead of creating a duplicate.")
        .Produces<Response>(201)
        .RequireAuthorization();
    }

    public record Request(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        CreateStore.StoreSelectorDto Selectors,
        string? PriceLocale = "en-US",
        bool RequiresJavaScript = false,
        string? CurrencyOverride = null
    );

    public record Command(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        CreateStore.StoreSelectorDto Selectors,
        string? PriceLocale = "en-US",
        bool RequiresJavaScript = false,
        string? CurrencyOverride = null
    )
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string StoreId,
        string Name,
        DateTime CreatedAt
    );

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
        }
    }

    public class Handler(OphiDbContext dbContext, IStoreConfigProvider configProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Check duplicate storeId
            var existingStore = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.StoreId == request.StoreId, cancellationToken);

            if (existingStore != null)
                throw new ApiException("A store with this ID already exists", 409, "Conflict");

            // Check domain overlap (case-insensitive, consistent with CreateStore/UpdateStore)
            await StoreValidationHelper.CheckDomainOverlapAsync(dbContext, request.UserId, request.DomainPatterns, null, cancellationToken);

            // Validate PriceLocale (consistent with CreateStore)
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
                RequiresJavaScript = request.RequiresJavaScript,
                CurrencyOverride = request.CurrencyOverride
            };

            dbContext.StoreConfigurations.Add(storeConfiguration);
            await dbContext.SaveChangesAsync(cancellationToken);

            configProvider.InvalidateCache(request.UserId);

            logger.LogInformation("Store {StoreId} imported for user {UserId}", storeConfiguration.StoreId, request.UserId);
            return new Response(
                storeConfiguration.Id,
                storeConfiguration.StoreId,
                storeConfiguration.Name,
                storeConfiguration.CreatedAt
            );
        }
    }
}
