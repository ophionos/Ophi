using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Wolverine;

namespace Ophi.Api.Features.Challenges;

public static class StartChallenge
{
    public static void MapStartChallengeEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/{id:guid}/urls/{urlId:guid}/challenge",
                async (Guid id, Guid urlId, IMessageBus bus, HttpContext context) =>
                {
                    var result = await bus.InvokeAsync<Response>(new Command(id, urlId, context.User.GetUserId()));
                    return Results.Ok(result);
                })
            .WithName("StartChallenge")
            .WithTags("Challenges")
            .WithSummary("Open a challenge session for a blocked URL")
            .WithDescription("Opens the URL in a remote browser so the user can solve the store's anti-bot challenge. Only for a URL whose last check was blocked. Replaces the user's earlier session. Poll GET /api/v1/challenge for frames.")
            .Produces<Response>()
            .RequireAuthorization()
            .RequireRateLimiting(Common.RateLimitPolicies.OutboundFetch);
    }

    public record Command(Guid ProductId, Guid ProductUrlId, Guid UserId);

    public record Response(string Host);

    /// <summary>
    /// The <c>LastError</c> of a check that a challenge page stopped (CheckProductPriceHandler). Not a
    /// plain 403: without a challenge title the session cannot tell a solved page from a refusal.
    /// </summary>
    public static bool IsChallengeError(string? lastError) =>
        lastError?.Contains("anti-bot", StringComparison.OrdinalIgnoreCase) == true;

    public class Handler(OphiDbContext dbContext, IChallengeSessionManager sessions, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!sessions.IsEnabled)
                throw new ApiException("Challenge solving is not enabled on this server.", 400, "ChallengeUnavailable");

            var productUrl = await dbContext.ProductUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(pu => pu.Id == request.ProductUrlId &&
                                           pu.ProductId == request.ProductId &&
                                           pu.Product.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product URL not found");

            if (!IsChallengeError(productUrl.LastError))
                throw new ApiException("This URL was not blocked by an anti-bot challenge.", 400, "NotBlocked");

            try
            {
                var session = await sessions.StartAsync(request.UserId, productUrl.Url, cancellationToken);
                logger.LogInformation("Challenge session started for URL {ProductUrlId}", productUrl.Id);
                return new Response(session.Host);
            }
            catch (ChallengeLimitException ex)
            {
                throw new ApiException(ex.Message, 429, "TooManySessions");
            }
            catch (PlaywrightException ex)
            {
                logger.LogWarning(ex, "Challenge session could not open URL {ProductUrlId}", productUrl.Id);
                throw new ApiException("The store page could not be opened.", 502, "ChallengeOpenFailed");
            }
        }
    }
}
