using FluentValidation;
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
        .WithDescription("Imports a store configuration from JSON (exported from another instance or user). A store with the same store ID is not overwritten: the request fails with 409 Conflict.")
        .Produces<Response>(201)
        .RequireAuthorization();
    }

    public record Request(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        string? PriceLocale = "en-US",
        bool RequiresJavaScript = false,
        string? CurrencyOverride = null
    );

    public record Command(
        string StoreId,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
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

            RuleFor(x => x.DomainPatterns).MustBeValidDomainPatterns();

            RuleFor(x => x.PriceLocale).MustFitPriceLocale();

            RuleFor(x => x.Selectors).MustBeValidSelectors();

            RuleFor(x => x.CurrencyOverride).MustBeCurrencyCode();
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
