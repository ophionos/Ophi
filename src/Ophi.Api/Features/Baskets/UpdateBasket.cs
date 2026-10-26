using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Persistence.Configurations;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class UpdateBasket
{
    public record Request(string Name, string? Description = null);

    public record Command(Guid Id, string Name, string? Description = null)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Id)
                .NotEmpty();

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
            var basket = await dbContext.Baskets
                .FirstOrDefaultAsync(b => b.Id == request.Id && b.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket not found");

            basket.Rename(request.Name, request.Description);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Basket {BasketId} renamed", basket.Id);

            return await BasketReader.LoadAsync(dbContext, basket.Id, request.UserId, cancellationToken);
        }
    }

    public static void MapUpdateBasketEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/api/v1/baskets/{id:guid}", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.Name, request.Description)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<BasketDto>(command);
            return Results.Ok(result);
        })
        .WithName("UpdateBasket")
        .WithTags("Baskets")
        .WithSummary("Rename a basket")
        .WithDescription("Replaces the name and description of a basket. A missing description clears it. Items are not changed. Returns 404 for a basket that belongs to another user.")
        .Produces<BasketDto>(200)
        .RequireAuthorization();
}
