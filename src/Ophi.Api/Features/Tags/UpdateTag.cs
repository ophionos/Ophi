using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class UpdateTag
{
    public static void MapUpdateTagEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/tags/{id:guid}", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.Name, request.Color, request.Weight)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("UpdateTag")
        .WithTags("Tags")
        .WithSummary("Update a tag")
        .WithDescription("Updates a tag's name and/or color. The new name must remain unique among the user's tags.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Request(string? Name, string? Color, int? Weight);

    public record Command(Guid TagId, string? Name, string? Color, int? Weight)
    {
        public Guid UserId { get; init; }
    }

    public record Response(Guid Id, string Name, string Color, int Weight);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(50)
                .When(x => x.Name != null);

            RuleFor(x => x.Color)
                .Matches(@"^#[0-9A-Fa-f]{6}$")
                .When(x => x.Color != null)
                .WithMessage("Color must be a valid hex color (e.g., #3B82F6)");
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var tag = await dbContext.Tags
                .FirstOrDefaultAsync(t => t.Id == request.TagId && t.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Tag not found");

            if (request.Name != null)
            {
                // Check for duplicate name
                var existingTag = await dbContext.Tags
                    .FirstOrDefaultAsync(t => t.UserId == request.UserId && t.Name == request.Name && t.Id != request.TagId, cancellationToken);

                if (existingTag != null)
                {
                    throw new ApiException("A tag with this name already exists", 409, "Conflict");
                }

                tag.Name = request.Name;
            }

            if (request.Color != null)
            {
                tag.Color = request.Color;
            }

            if (request.Weight.HasValue)
            {
                tag.Weight = request.Weight.Value;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Tag {TagId} updated", request.TagId);
            return new Response(tag.Id, tag.Name, tag.Color, tag.Weight);
        }
    }
}
