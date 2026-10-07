using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class DeleteTag
{
    public static void MapDeleteTagEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/tags/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteTag")
        .WithTags("Tags")
        .WithSummary("Delete a tag")
        .WithDescription("Permanently deletes a tag. Removes the tag association from all products but does not delete the products themselves.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid TagId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var tag = await dbContext.Tags
                .FirstOrDefaultAsync(t => t.Id == request.TagId && t.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Tag not found");

            dbContext.Tags.Remove(tag);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Tag {TagId} deleted by user {UserId}", request.TagId, request.UserId);
        }
    }
}
