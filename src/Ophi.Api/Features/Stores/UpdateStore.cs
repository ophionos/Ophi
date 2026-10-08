using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class UpdateStore
{
    public static void MapUpdateStoreEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/stores/{id:guid}", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(
                id,
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
            return Results.Ok(result);
        })
        .WithName("UpdateStore")
        .WithTags("Stores")
        .WithSummary("Update a store configuration")
        .WithDescription("Updates an existing store configuration. All fields (name, domain, selectors, JavaScript requirement, currency override) can be modified.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Request(
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        string? PriceLocale = null,
        bool? RequiresJavaScript = null,
        string? CurrencyOverride = null,
        string? AffiliateParamName = null,
        string? AffiliateTag = null,
        string? CustomUserAgent = null
    );

    public record Command(
        Guid Id,
        string Name,
        string[] DomainPatterns,
        StoreSelectorDto Selectors,
        string? PriceLocale = null,
        bool? RequiresJavaScript = null,
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
        DateTime? CreatedAt,
        string PriceLocale,
        bool RequiresJavaScript,
        string? CurrencyOverride,
        DateTime UpdatedAt,
        string? AffiliateParamName,
        string? AffiliateTag,
        string? CustomUserAgent
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Store ID is required");

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
            var store = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.Id == request.Id && s.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Store not found");
            
            // Check for duplicate domain patterns across user's other stores (exclude current store)
            await StoreValidationHelper.CheckDomainOverlapAsync(dbContext, request.UserId, request.DomainPatterns, request.Id, cancellationToken);

            var selectorConfig = request.Selectors.ToConfig();

            // Validate PriceLocale if provided
            if (request.PriceLocale != null)
            {
                StoreValidationHelper.ValidatePriceLocale(request.PriceLocale);
                store.PriceLocale = request.PriceLocale;
            }

            store.Name = request.Name;
            store.DomainPatternsJson = StoreValidationHelper.SerializeDomainPatterns(request.DomainPatterns);
            store.SelectorsJson = StoreValidationHelper.SerializeSelectors(selectorConfig);
            if (request.RequiresJavaScript.HasValue)
                store.RequiresJavaScript = request.RequiresJavaScript.Value;

            store.CurrencyOverride = request.CurrencyOverride;
            store.AffiliateParamName = request.AffiliateParamName;
            store.AffiliateTag = request.AffiliateTag;
            store.CustomUserAgent = request.CustomUserAgent;

            await dbContext.SaveChangesAsync(cancellationToken);

            // Invalidate cache for this user
            configProvider.InvalidateCache(request.UserId);

            logger.LogInformation("Store {StoreId} updated", store.StoreId);

            return new Response(
                store.Id,
                store.StoreId,
                store.Name,
                request.DomainPatterns,
                request.Selectors,
                false,
                store.IsAutoCreated,
                store.CreatedAt,
                store.PriceLocale,
                store.RequiresJavaScript,
                store.CurrencyOverride,
                store.UpdatedAt,
                store.AffiliateParamName,
                store.AffiliateTag,
                store.CustomUserAgent
            );
        }
    }
}
