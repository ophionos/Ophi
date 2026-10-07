using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Scraping;

public static class GetScrapeHealth
{
    public static void MapGetScrapeHealthEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/scrape-health", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetScrapeHealth")
        .WithTags("Scraping")
        .WithSummary("Get per-domain scrape health stats")
        .WithDescription("Returns aggregate scrape health statistics per store domain for the last 7 days. Includes success rate, duration percentiles, and anomaly counts.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query
    {
        public Guid UserId { get; init; }
    }

    public record Response(List<DomainHealth> Domains, ScrapeHealthSummary Summary);

    public record DomainHealth(
        string Domain,
        int TotalScrapes,
        int SuccessCount,
        int FailureCount,
        double SuccessRate,
        int ScrapesToday,
        double P50DurationMs,
        double P95DurationMs,
        string? LastFailureMessage,
        DateTime? LastFailureAt,
        DateTime? LastScrapeAt
    );

    public record ScrapeHealthSummary(
        int TotalDomains,
        int TotalScrapes7d,
        // Null when no scrapes fall in the window — there is no rate to report. Distinct from 0.0,
        // which is a real "everything failed". Clients must render null as "no data", not as 100%.
        double? OverallSuccessRate,
        int DomainsHealthy,
        int DomainsDegraded,
        int DomainsUnhealthy
    );

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching scrape health for user {UserId}", query.UserId);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now.AddDays(-7);
            var todayCutoff = now.Date;

            // Get all product IDs for this user
            var userProductIds = await dbContext.Products
                .AsNoTracking()
                .Where(p => p.UserId == query.UserId)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            if (userProductIds.Count == 0)
            {
                return new Response([], new ScrapeHealthSummary(0, 0, null, 0, 0, 0));
            }

            // Fetch scrape logs for user's products in the last 7 days
            var logs = await dbContext.ScrapeLogs
                .AsNoTracking()
                .Where(s => userProductIds.Contains(s.ProductId) && s.CreatedAt >= cutoff)
                .Select(s => new
                {
                    s.StoreDomain,
                    s.Success,
                    s.DurationMs,
                    s.Error,
                    s.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var domains = logs
                .GroupBy(l => string.IsNullOrEmpty(l.StoreDomain) ? "unknown" : l.StoreDomain)
                .Select(g =>
                {
                    var all = g.ToList();
                    var successCount = all.Count(l => l.Success);
                    var failureCount = all.Count - successCount;
                    var successRate = all.Count > 0 ? (double)successCount / all.Count : 1.0;
                    var scrapesToday = all.Count(l => l.CreatedAt >= todayCutoff);

                    var durations = all.Where(l => l.Success).Select(l => (double)l.DurationMs).OrderBy(d => d).ToList();
                    var p50 = Percentile(durations, 0.50);
                    var p95 = Percentile(durations, 0.95);

                    var lastFailure = all.Where(l => !l.Success).OrderByDescending(l => l.CreatedAt).FirstOrDefault();
                    var lastScrape = all.OrderByDescending(l => l.CreatedAt).FirstOrDefault();

                    return new DomainHealth(
                        Domain: g.Key,
                        TotalScrapes: all.Count,
                        SuccessCount: successCount,
                        FailureCount: failureCount,
                        SuccessRate: Math.Round(successRate, 4),
                        ScrapesToday: scrapesToday,
                        P50DurationMs: Math.Round(p50, 1),
                        P95DurationMs: Math.Round(p95, 1),
                        LastFailureMessage: lastFailure?.Error,
                        LastFailureAt: lastFailure?.CreatedAt,
                        LastScrapeAt: lastScrape?.CreatedAt
                    );
                })
                .OrderByDescending(d => d.TotalScrapes)
                .ToList();

            var totalScrapes = domains.Sum(d => d.TotalScrapes);
            var totalSuccess = domains.Sum(d => d.SuccessCount);
            double? overallRate = totalScrapes > 0 ? (double)totalSuccess / totalScrapes : null;

            var summary = new ScrapeHealthSummary(
                TotalDomains: domains.Count,
                TotalScrapes7d: totalScrapes,
                OverallSuccessRate: overallRate is null ? null : Math.Round(overallRate.Value, 4),
                DomainsHealthy: domains.Count(d => d.SuccessRate >= 0.95),
                DomainsDegraded: domains.Count(d => d.SuccessRate >= 0.80 && d.SuccessRate < 0.95),
                DomainsUnhealthy: domains.Count(d => d.SuccessRate < 0.80)
            );

            logger.LogDebug("Returning scrape health: {DomainCount} domains, {TotalScrapes} scrapes for user {UserId}",
                domains.Count, totalScrapes, query.UserId);

            return new Response(domains, summary);
        }

        private static double Percentile(List<double> sorted, double percentile)
        {
            if (sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];

            var index = percentile * (sorted.Count - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            var weight = index - lower;

            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
        }
    }
}
