using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class AddProduct
{
    public static void MapAddProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Url)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Accepted($"/api/v1/products/{result.Id}", result);
        })
        .WithName("AddProduct")
        .WithTags("Products")
        .WithSummary("Add a product by URL")
        .WithDescription("Submits a URL for asynchronous product scraping. Returns 202 with a pending product immediately. The worker extracts the product name, price, image, and currency in the background. Poll the product by ID to check completion status.")
        .Produces<Response>(202)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.ProductCreation);
    }


    public record Request(string Url);

    public record Command(string Url)
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Name,
        string Url,
        string? ImageUrl,
        decimal? CurrentPrice,
        string Currency,
        DateTime? LastChecked,
        string Status
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Url)
                .NotEmpty()
                .MaximumLength(2048).WithMessage("URL must not exceed 2048 characters")
                .MustBeValidHttpUrl();
        }
    }

    public class Handler(OphiDbContext dbContext, IMessageBus messageBus, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Duplicate check by ProductUrlKey, across all the user's products
            var tracked = await TrackedUrlIndex.LoadAsync(dbContext, request.UserId, cancellationToken);
            if (tracked.Find(request.Url) is { } existing)
            {
                logger.LogWarning("Duplicate product URL for user {UserId}: {Url}", request.UserId, request.Url);
                throw new ConflictException("You are already tracking this product", existing.ProductId, existing.ProductUrlId);
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = "Loading...",
                ImageUrl = null,
                CurrentPrice = null,
                Currency = "USD",
                Status = ProductStatus.Pending
            };

            var productUrl = new ProductUrl
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Url = request.Url,
                Currency = "USD",
                SelectorType = SelectorType.Auto
            };

            dbContext.Products.Add(product);
            dbContext.ProductUrls.Add(productUrl);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                throw new ConflictException("You are already tracking this product");
            }

            // Instant trigger: publish the scrape request. In split mode this routes durably over the
            // Postgres transport to the worker; in embedded mode it's handled in-process. Reliability
            // does NOT depend on this publish succeeding — Status=Pending + the worker's reconciliation
            // poll (DispatchPendingProductsAsync) is the backstop if the publish is ever lost.
            await messageBus.PublishAsync(new ScrapeProductUrlCommand(productUrl.Id));

            logger.LogInformation("Product {ProductId} created for user {UserId} from URL {Url}", product.Id, request.UserId, request.Url);
            return new Response(
                product.Id,
                product.Name,
                productUrl.Url,
                product.ImageUrl,
                product.CurrentPrice,
                product.Currency,
                productUrl.LastCheckedAt,
                product.Status.ToApiString()
            );
        }
    }
}
