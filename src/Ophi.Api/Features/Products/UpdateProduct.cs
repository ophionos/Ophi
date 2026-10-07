using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class UpdateProduct
{
    public static void MapUpdateProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/products/{id:guid}", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(
                id,
                request.Name,
                request.ImageUrl,
                request.Status,
                request.IsFavourite,
                request.CustomFields,
                request.CheckIntervalMinutes
            )
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("UpdateProduct")
        .WithTags("Products")
        .WithSummary("Update a product")
        .WithDescription("Updates product fields including name, status (active/paused), favourite flag, custom fields, check interval, and comparison group assignment. Only provided fields are updated. Set checkIntervalMinutes to 0 to reset to user/system default.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }


    public record Request(
        string? Name,
        string? ImageUrl,
        string? Status,
        bool? IsFavourite,
        List<CustomFieldDto>? CustomFields = null,
        int? CheckIntervalMinutes = null
    );

    public record Command(
        Guid ProductId,
        string? Name,
        string? ImageUrl,
        string? Status,
        bool? IsFavourite,
        List<CustomFieldDto>? CustomFields = null,
        int? CheckIntervalMinutes = null
    )
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Name,
        string Url,
        string? ImageUrl,
        decimal? CurrentPrice,
        decimal? PreviousPrice,
        decimal? PriceChange,
        string Currency,
        DateTime? LastChecked,
        string Status,
        bool IsFavourite,
        List<CustomFieldDto> CustomFields,
        int? CheckIntervalMinutes
    );

    public class Validator : AbstractValidator<Command>
    {
        private static readonly string[] AllowedStatuses =
        [
            ProductStatus.Active.ToApiString(),
            ProductStatus.Paused.ToApiString()
        ];

        public Validator()
        {
            RuleFor(x => x)
                .Must(x => x.Name != null || x.ImageUrl != null || x.Status != null || x.IsFavourite.HasValue || x.CustomFields != null || x.CheckIntervalMinutes.HasValue)
                .WithMessage("At least one field must be provided");

            When(x => x.Name != null, () =>
            {
                RuleFor(x => x.Name!)
                    .NotEmpty().WithMessage("Name cannot be empty")
                    .MaximumLength(500).WithMessage("Name must not exceed 500 characters");
            });

            When(x => x.ImageUrl != null, () =>
            {
                RuleFor(x => x.ImageUrl!)
                    .MaximumLength(2048).WithMessage("Image URL must not exceed 2048 characters")
                    .MustBeValidHttpUrl();
            });

            When(x => x.Status != null, () =>
            {
                RuleFor(x => x.Status!)
                    .Must(s => AllowedStatuses.Contains(s.ToLowerInvariant()))
                    .WithMessage("Status must be 'active' or 'paused'");
            });

            When(x => x.CheckIntervalMinutes.HasValue && x.CheckIntervalMinutes.Value != 0, () =>
            {
                RuleFor(x => x.CheckIntervalMinutes)
                    .Must(v => v >= ProductValidationRules.CheckIntervalMinutesMin && v <= ProductValidationRules.CheckIntervalMinutesMax)
                    .WithMessage($"Check interval must be between {ProductValidationRules.CheckIntervalMinutesMin} and {ProductValidationRules.CheckIntervalMinutesMax} minutes (or 0 to reset to default)");
            });

            ProductValidationRules.ApplyCustomFieldsRules(this, x => x.CustomFields);
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        private static readonly ProductStatus[] EditableStatuses = [ProductStatus.Active, ProductStatus.Paused];

        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .Include(p => p.ProductUrls)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            // Validate status transition if status change is requested
            if (request.Status != null)
            {
                if (!EditableStatuses.Contains(product.Status))
                {
                    throw new ApiException(
                        $"Cannot change status of a product in '{product.Status.ToApiString()}' state",
                        400,
                        "Bad Request");
                }

                switch (Enum.Parse<ProductStatus>(request.Status, ignoreCase: true))
                {
                    case ProductStatus.Active: product.Resume(); break;
                    case ProductStatus.Paused: product.Pause(); break;
                    default: throw new ApiException("Invalid status transition", 400, "Bad Request");
                }
            }

            if (request.Name != null)
            {
                product.Name = request.Name;
            }

            if (request.ImageUrl != null)
            {
                product.ImageUrl = request.ImageUrl;
            }

            if (request.IsFavourite.HasValue)
            {
                product.IsFavourite = request.IsFavourite.Value;
            }

            if (request.CustomFields != null)
            {
                product.CustomFields = request.CustomFields
                    .Select(cf => new CustomField(cf.Name, cf.Value))
                    .ToList();
            }

            if (request.CheckIntervalMinutes.HasValue)
            {
                product.CheckIntervalMinutes = request.CheckIntervalMinutes.Value == 0
                    ? null
                    : request.CheckIntervalMinutes.Value;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Product {ProductId} updated by user {UserId}", request.ProductId, request.UserId);

            // Calculate price change
            decimal? priceChange = null;
            if (product is { CurrentPrice: not null, PreviousPrice: not null and not 0 })
            {
                priceChange = Math.Round(
                    (product.CurrentPrice.Value - product.PreviousPrice.Value) / product.PreviousPrice.Value * 100,
                    2);
            }

            var firstUrl = product.GetPrimaryUrl();
            var lastChecked = product.ProductUrls.Count != 0 ? product.ProductUrls.Max(pu => pu.LastCheckedAt) : null;

            return new Response(
                product.Id,
                product.Name,
                firstUrl?.Url ?? "",
                product.ImageUrl,
                product.CurrentPrice,
                product.PreviousPrice,
                priceChange,
                product.Currency,
                lastChecked,
                product.Status.ToApiString(),
                product.IsFavourite,
                product.CustomFields.Select(cf => new CustomFieldDto(cf.Name, cf.Value)).ToList(),
                product.CheckIntervalMinutes
            );
        }
    }
}
