using JasperFx.CodeGeneration.Model;
using JasperFx.Resources;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Worker.Handlers;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace Ophi.Worker.Configuration;

/// <summary>
/// Single source of truth for Wolverine messaging config across the three deployment shapes, so the
/// API and Worker <c>Program.cs</c> files stop duplicating it (and diverging).
///
/// - <see cref="WolverineMode.Embedded"/> — API runs the worker in-process (Pi / single container).
///   Handles <c>ScrapeProductUrlCommand</c> locally via <c>[LocalQueue("scraping")]</c>.
/// - <see cref="WolverineMode.SplitApi"/> — API with no in-process worker. Routes
///   <c>ScrapeProductUrlCommand</c> out to the Postgres transport queue.
/// - <see cref="WolverineMode.SplitWorker"/> — standalone worker. Listens on that queue and runs the
///   real handlers.
///
/// Durability is gated on a Postgres connection string being <b>present</b> (not on DB_PROVIDER, which
/// defaults to postgres and is unset in the Testing env). Absent → Wolverine stays in-memory exactly as
/// before, so the SQLite test tiers are untouched.
/// </summary>
public enum WolverineMode { Embedded, SplitApi, SplitWorker }

public static class WolverineConfig
{
    /// <summary>
    /// Postgres-transport queue carrying <c>ScrapeProductUrlCommand</c> API → Worker in split mode.
    /// Deliberately distinct from the in-process <c>LocalQueue("scraping")</c> to avoid an
    /// endpoint-name clash in the shared <c>wolverine_*</c> storage.
    /// </summary>
    public const string ScrapeRequestQueue = "scrape-requests";

    /// <summary>
    /// Postgres-transport queue carrying <c>LiveUpdate</c> Worker → API in split mode, so the API can
    /// fan it out to that user's open SSE streams. Reverse direction of <see cref="ScrapeRequestQueue"/>.
    ///
    /// <para>
    /// Competing-consumer semantics: each message is delivered to exactly one listener. That is correct
    /// with a single API replica. If the API is ever scaled horizontally, a user's <c>EventSource</c>
    /// pins to one replica while this queue may hand the message to another — at that point this hop must
    /// move to Postgres LISTEN/NOTIFY (broadcast) so every replica's locally-connected clients get it.
    /// </para>
    /// </summary>
    public const string ScrapeNotificationQueue = "scrape-notifications";

    public static void Configure(WolverineOptions opts, WolverineMode mode, string? postgresConnectionString)
    {
        // Wolverine 6 defaults ServiceLocationPolicy to NotAllowed; our handlers inject the scoped
        // OphiDbContext (opaque factory codegen can't inline-construct), so allow it globally.
        opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

        var durable = !string.IsNullOrWhiteSpace(postgresConnectionString);
        if (durable)
        {
            // Postgres message storage + DB-backed transport (no broker). AutoProvision creates the
            // wolverine_* envelope tables at startup (idempotent), managed separately from EF's
            // app-table migrations — a deliberate two-schema-manager split. Startup already requires
            // Postgres reachable (EF Migrate + depends_on: db healthy), so no new ordering constraint.
            opts.UsePostgresqlPersistenceAndTransport(postgresConnectionString!).AutoProvision();
            opts.UseEntityFrameworkCoreTransactions();
        }

        if (mode is WolverineMode.Embedded or WolverineMode.SplitWorker)
        {
            ConfigureWorkerQueues(opts);
            // Discover the worker handlers (this assembly).
            opts.Discovery.IncludeAssembly(typeof(ScrapeNewProductHandler).Assembly);
        }

        switch (mode)
        {
            case WolverineMode.SplitWorker when durable:
                // Ingress for cross-process scrape requests. Wolverine dispatches the received command
                // to ScrapeNewProductHandler ([LocalQueue("scraping")]).
                opts.ListenToPostgresqlQueue(ScrapeRequestQueue);
                // Egress for SSE pings. The fan-out handler lives in the Ophi.Api assembly, which this
                // process doesn't discover, so routing LiveUpdate out (rather than handling it locally)
                // is what keeps the worker from trying to fan out to streams it doesn't host.
                opts.PublishMessage<LiveUpdate>().ToPostgresqlQueue(ScrapeNotificationQueue);
                break;

            case WolverineMode.SplitApi when durable:
                // Durable, instant API → Worker hop, replacing the poll-interval bridge latency.
                opts.PublishMessage<ScrapeProductUrlCommand>().ToPostgresqlQueue(ScrapeRequestQueue);
                // Ingress for SSE pings from the worker; PushLiveUpdateHandler (Ophi.Api) fans them out.
                opts.ListenToPostgresqlQueue(ScrapeNotificationQueue);
                break;
        }
    }

    private static void ConfigureWorkerQueues(WolverineOptions opts)
    {
        opts.Policies.UseDurableLocalQueues();

        opts.LocalQueue("scraping").MaximumParallelMessages(3);
        // Serial within this process so concurrent PriceUpdatedEvents can't both pass the alert
        // cooldown check before either persists LastTriggeredAt. This is now just an in-process
        // optimization, NOT the thing closing the race: the cross-worker guard is the xmin optimistic
        // concurrency token on Alert plus the DbUpdateConcurrencyException catch in CheckAlertsHandler,
        // so multiple worker processes are safe. Safe to raise this, or scale workers horizontally.
        opts.LocalQueue("events").MaximumParallelMessages(1);
        opts.LocalQueue("notifications").MaximumParallelMessages(2);

        opts.Policies
            .OnException<HttpRequestException>()
            .RetryWithCooldown(
                Jitter(TimeSpan.FromSeconds(5)),
                Jitter(TimeSpan.FromSeconds(30)),
                Jitter(TimeSpan.FromMinutes(2)));

        // Catch-all: retry twice then move to the error queue for later inspection/replay.
        opts.Policies
            .OnException<Exception>()
            .RetryWithCooldown(
                Jitter(TimeSpan.FromSeconds(30)),
                Jitter(TimeSpan.FromMinutes(5)))
            .Then.MoveToErrorQueue();
    }

    // ±20% jitter to prevent a thundering-herd of synchronized retries.
    private static TimeSpan Jitter(TimeSpan t)
    {
        var factor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromMilliseconds(t.TotalMilliseconds * factor);
    }
}
