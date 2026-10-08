using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Features.Products;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Wolverine;

namespace Ophi.Api.Features.Challenges;

public static class GetChallenge
{
    public static void MapGetChallengeEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/challenge", async (IMessageBus bus, HttpContext context) =>
            {
                var userId = context.User.GetUserId();
                var result = await bus.InvokeAsync<Result>(new Query(userId));

                // Top-level invokes, one scope each: the retry handler owns its own DbContext.
                foreach (var (productId, urlId) in result.Retry)
                    await bus.InvokeAsync(new RetryScrapeProductUrl.Command(productId, urlId, userId));

                return Results.Ok(result.Response);
            })
            .WithName("GetChallenge")
            .WithTags("Challenges")
            .WithSummary("Get the challenge session's current frame")
            .WithDescription("Returns the user's challenge session state. While the challenge is unsolved, 'active' with a JPEG frame (base64) of the 1920x1080 page. When the page is past the challenge, saves its cookies for this store, closes the session, re-checks the user's blocked URLs on that host, and returns 'solved'. 'none' when there is no session.")
            .Produces<Response>()
            .RequireAuthorization()
            // Polled twice a second while a session is open: the global per-user limit would stop it.
            // A request without an open session costs one dictionary lookup.
            .DisableRateLimiting();
    }

    public record Query(Guid UserId);

    /// <param name="State">none, active, or solved.</param>
    public record Response(string State, string? Image = null, string? Host = null);

    public record Result(Response Response, IReadOnlyList<(Guid ProductId, Guid ProductUrlId)> Retry);

    public class Handler(
        OphiDbContext dbContext,
        IChallengeSessionManager sessions,
        IStoreClearanceStore clearances,
        ILogger<Handler> logger)
    {
        public async Task<Result> Handle(Query request, CancellationToken cancellationToken)
        {
            var session = sessions.Find(request.UserId);
            if (session == null)
                return new Result(new Response("none"), []);

            string state;
            try
            {
                if (!await session.IsClearedAsync())
                {
                    var frame = await session.ScreenshotAsync();
                    return new Result(new Response("active", Convert.ToBase64String(frame), session.Host), []);
                }
                state = await session.StorageStateAsync();
            }
            catch (PlaywrightException ex)
            {
                // Usually a navigation in progress after a click; the next poll gets a frame.
                logger.LogDebug(ex, "Challenge frame unavailable for {Host}", session.Host);
                return new Result(new Response("active", Host: session.Host), []);
            }

            await clearances.SaveAsync(request.UserId, session.Host, state, session.UserAgent, cancellationToken);
            await sessions.CloseAsync(request.UserId);

            // Every URL of this user on the host that a challenge stopped, not only the one the session
            // opened: one solve clears the store.
            var candidates = await dbContext.ProductUrls
                .AsNoTracking()
                .Where(pu => pu.Product.UserId == request.UserId && pu.LastError != null)
                .Select(pu => new { pu.Id, pu.ProductId, pu.Url, pu.LastError })
                .ToListAsync(cancellationToken);
            var retry = candidates
                .Where(c => StartChallenge.IsChallengeError(c.LastError) &&
                            Uri.TryCreate(c.Url, UriKind.Absolute, out var uri) &&
                            string.Equals(uri.Host, session.Host, StringComparison.OrdinalIgnoreCase))
                .Select(c => (c.ProductId, c.Id))
                .ToList();

            logger.LogInformation("Challenge solved for {Host}; re-checking {Count} URL(s)", session.Host, retry.Count);
            return new Result(new Response("solved", Host: session.Host), retry);
        }
    }
}
