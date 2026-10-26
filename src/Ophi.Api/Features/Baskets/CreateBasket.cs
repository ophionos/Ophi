using FluentValidation;
using Ophi.Domain.Entities;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class CreateBasket
{
    public record Request(string Name, string? Description = null);

    public record Command(string Name, string? Description = null)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(BasketConfiguration.NameMaxLength);

            RuleFor(x => x.Description)
                .MaximumLength(BasketConfiguration.DescriptionMaxLength);
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<BasketDto> Handle(Command request, CancellationToken cancellationToken)
        {
            var basket = new Basket
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                Description = request.Description
            };

            dbContext.Baskets.Add(basket);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Basket {BasketId} created for user {UserId}", basket.Id, request.UserId);

            return await BasketReader.LoadAsync(dbContext, basket.Id, request.UserId, cancellationToken);
        }
    }

    public static void MapCreateBasketEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/baskets", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Description)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<BasketDto>(command);
            return Results.Created($"/api/v1/baskets/{result.Id}", result);
        })
        .WithName("CreateBasket")
        .WithTags("Baskets")
        .WithSummary("Create a basket")
        .WithDescription("Creates an empty basket with a name and an optional description. A basket holds products bought together, each with a quantity. Add products afterwards.")
        .Produces<BasketDto>(201)
        .RequireAuthorization();
}
