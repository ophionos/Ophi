using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class AddProductUrl
{
    public static void MapAddProductUrlEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/{id:guid}/urls", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.Url)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/products/{id}/urls/{result.Id}", result);
        })
        .WithName("AddProductUrl")
        .WithTags("Products")
        .WithSummary("Add a URL to a product")
        .WithDescription("Adds an additional tracking URL to an existing product. The URL is scraped asynchronously. Duplicate URLs across the user's products are rejected. Enables price comparison across multiple stores for the same product.")
        .Produces<Response>(201)
        .RequireAuthorization();
    }

    public record Request(string Url);

    public record Command(Guid ProductId, string Url)
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Url,
        string? StoreId,
        decimal? CurrentPrice,
        string Currency,
        DateTime? LastCheckedAt,
        string? LastError,
        int FailureCount
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Url)
                .NotEmpty()
                .MaximumLength(2048).WithMessage("URL must not exceed 2048 characters")
                .MustBeValidHttpUrl();

            RuleFor(x => x.ProductId)
                .NotEmpty();
        }
    }

    public class Handler(OphiDbContext dbContext, IMessageBus messageBus, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            // Duplicate check by ProductUrlKey, across all the user's products — so the 409 names the
            // product that holds the URL, which need not be this one.
            var tracked = await TrackedUrlIndex.LoadAsync(dbContext, request.UserId, cancellationToken);
            if (tracked.Find(request.Url) is { } existing)
            {
                throw new ConflictException("This URL is already tracked", existing.ProductId, existing.ProductUrlId);
            }

            var productUrl = new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = request.Url,
                Currency = product.Currency,
                SelectorType = SelectorType.Auto
            };

            dbContext.ProductUrls.Add(productUrl);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                throw new ConflictException("This URL is already tracked");
            }

            // Queue for scraping
            await messageBus.PublishAsync(new ScrapeProductUrlCommand(productUrl.Id));

            logger.LogInformation("URL {ProductUrlId} added to product {ProductId}: {Url}", productUrl.Id, product.Id, request.Url);
            return new Response(
                productUrl.Id,
                productUrl.Url,
                productUrl.StoreId,
                productUrl.CurrentPrice,
                productUrl.Currency,
                productUrl.LastCheckedAt,
                productUrl.LastError,
                productUrl.FailureCount
            );
        }
    }
}
