using FluentValidation;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class CreateProduct
{
    public static void MapCreateProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/create", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.ImageUrl, request.Currency, request.CustomFields)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/products/{result.Id}", result);
        })
        .WithName("CreateProduct")
        .WithTags("Products")
        .WithSummary("Create a product manually")
        .WithDescription("Creates a product with manually provided details (name, URL, price, currency, image). Unlike AddProduct, no scraping is performed. Useful for products that cannot be automatically scraped.")
        .Produces<Response>(201)
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.ProductCreation);
    }

    public record Request(string Name, string? ImageUrl = null, string? Currency = null, List<CustomFieldDto>? CustomFields = null);

    public record Command(string Name, string? ImageUrl = null, string? Currency = null, List<CustomFieldDto>? CustomFields = null)
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Name,
        string? ImageUrl,
        decimal? CurrentPrice,
        string Currency,
        string Status,
        List<CustomFieldDto> CustomFields
    );

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(500);

            When(x => !string.IsNullOrEmpty(x.ImageUrl), () =>
            {
                RuleFor(x => x.ImageUrl!)
                    .MaximumLength(2048)
                    .MustBeValidHttpUrl();
            });

            RuleFor(x => x.Currency)
                .Matches(ProductValidationRules.CurrencyPattern)
                .WithMessage(ProductValidationRules.CurrencyMessage)
                .When(x => !string.IsNullOrEmpty(x.Currency));

            ProductValidationRules.ApplyCustomFieldsRules(this, x => x.CustomFields);
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                ImageUrl = request.ImageUrl,
                CurrentPrice = null,
                Currency = request.Currency ?? "USD",
                Status = ProductStatus.Active,
                CustomFields = request.CustomFields?
                    .Select(cf => new CustomField(cf.Name, cf.Value))
                    .ToList() ?? []
            };

            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Product {ProductId} created manually for user {UserId}", product.Id, request.UserId);
            return new Response(
                product.Id,
                product.Name,
                product.ImageUrl,
                product.CurrentPrice,
                product.Currency,
                product.Status.ToApiString(),
                product.CustomFields.Select(cf => new CustomFieldDto(cf.Name, cf.Value)).ToList()
            );
        }
    }
}
