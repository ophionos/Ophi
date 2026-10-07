using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class DeleteComparisonGroup
{
    public record Command(Guid GroupId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var group = await dbContext.ComparisonGroups
                .FirstOrDefaultAsync(cg => cg.Id == request.GroupId && cg.UserId == request.UserId, cancellationToken) ??
                throw new NotFoundException("Comparison group not found");

            dbContext.ComparisonGroups.Remove(group);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Comparison group {GroupId} deleted", request.GroupId);
        }
    }

    public static void MapDeleteComparisonGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/comparisons/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteComparisonGroup")
        .WithTags("Comparisons")
        .WithSummary("Delete a comparison group")
        .WithDescription("Permanently deletes a comparison group. Products in the group are disassociated but not deleted.")
        .Produces(204)
        .RequireAuthorization();
}
