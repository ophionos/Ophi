using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class CreateTag
{
    public static void MapCreateTagEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/tags", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Color, request.Weight)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/tags/{result.Id}", result);
        })
        .WithName("CreateTag")
        .WithTags("Tags")
        .WithSummary("Create a tag")
        .WithDescription("Creates a new tag with a name and optional hex color code. Tags are used to categorize and filter products. Tag names must be unique per user.")
        .Produces<Response>(201)
        .RequireAuthorization();
    }

    public record Request(string Name, string? Color, int? Weight);

    public record Command(string Name, string? Color, int? Weight)
    {
        public Guid UserId { get; init; }
    }

    public record Response(Guid Id, string Name, string Color, int Weight);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(50);

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
            var existingTag = await dbContext.Tags
                .FirstOrDefaultAsync(t => t.UserId == request.UserId && t.Name == request.Name, cancellationToken);

            if (existingTag != null)
            {
                throw new ApiException("A tag with this name already exists", 409, "Conflict");
            }

            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                Color = request.Color ?? "#3B82F6",
                Weight = request.Weight ?? 0
            };

            dbContext.Tags.Add(tag);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Tag {TagId} '{TagName}' created for user {UserId}", tag.Id, tag.Name, request.UserId);
            return new Response(tag.Id, tag.Name, tag.Color, tag.Weight);
        }
    }
}
