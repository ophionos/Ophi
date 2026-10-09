using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Api.Common.Validators;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class LookupProduct
{
    public static void MapLookupProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products/lookup", async (string url, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(new Query(context.User.GetUserId(), url));
            return Results.Ok(result);
        })
        .WithName("LookupProduct")
        .WithTags("Products")
        .WithSummary("Find the tracked product for a URL")
        .WithDescription("Returns the caller's product that tracks the given URL, or 404. URLs match by comparison key: known tracking parameters, the fragment, the scheme, host case and a leading \"www.\" are ignored; any other query parameter (such as a variant) must match.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId, string Url);

    public record Response(
        Guid ProductId,
        Guid ProductUrlId,
        string Name,
        string Url,
        decimal? CurrentPrice,
        string Currency,
        string Status
    );

    public class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(x => x.Url)
                .NotEmpty()
                .MaximumLength(2048).WithMessage("URL must not exceed 2048 characters")
                .MustBeValidHttpUrl();
        }
    }

    public class Handler(OphiDbContext dbContext)
    {
        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            var tracked = await TrackedUrlIndex.LoadAsync(dbContext, query.UserId, cancellationToken);
            var match = tracked.Find(query.Url) ?? throw new NotFoundException("This URL is not tracked");

            var found = await dbContext.ProductUrls
                .Where(pu => pu.Id == match.ProductUrlId)
                .Select(pu => new { pu.Product.Name, pu.Url, pu.Product.CurrentPrice, pu.Product.Currency, pu.Product.Status })
                .FirstAsync(cancellationToken);

            return new Response(match.ProductId, match.ProductUrlId, found.Name, found.Url,
                found.CurrentPrice, found.Currency, found.Status.ToApiString());
        }
    }
}
