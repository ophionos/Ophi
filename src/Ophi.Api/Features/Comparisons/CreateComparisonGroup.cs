using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class CreateComparisonGroup
{
    public record Request(string Name, string? Description = null);

    public record Command(string Name, string? Description = null)
    {
        public Guid UserId { get; init; }
    }

    public record Response(Guid Id, string Name, int ProductCount);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Description)
                .MaximumLength(500);
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var existingGroup = await dbContext.ComparisonGroups
                .FirstOrDefaultAsync(cg => cg.UserId == request.UserId && cg.Name == request.Name, cancellationToken);

            if (existingGroup != null)
            {
                throw new ApiException("A comparison group with this name already exists", 409, "Conflict");
            }

            var group = new ComparisonGroup
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                Description = request.Description
            };

            dbContext.ComparisonGroups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Comparison group {GroupId} created for user {UserId}", group.Id, request.UserId);

            return new Response(group.Id, group.Name, 0);
        }
    }

    public static void MapCreateComparisonGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/comparisons", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Description)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/comparisons/{result.Id}", result);
        })
        .WithName("CreateComparisonGroup")
        .WithTags("Comparisons")
        .WithSummary("Create a comparison group")
        .WithDescription("Creates a named group for comparing prices of similar products across different stores. Optionally include product IDs to add to the group at creation time.")
        .Produces<Response>(201)
        .RequireAuthorization();
}
