using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ophi.Api.Common.Events;
using Ophi.Api.Features.Events;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Worker.Configuration;
using Wolverine;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The SSE counterpart to <see cref="CrossProcessDeliveryTests"/>: a <see cref="LiveUpdate"/> emitted by
/// the split <b>Worker</b> must cross the Postgres transport (queue <c>scrape-notifications</c>) to the
/// split <b>API</b>, where <see cref="PushLiveUpdateHandler"/> fans it out to that user's open SSE stream
/// via <see cref="SseConnectionRegistry"/>.
///
/// <para>
/// This is the only coverage for the second cross-process hop. The in-process tests prove the emitters
/// and the fan-out handler in isolation, and <see cref="CrossProcessDeliveryTests"/> proves the transport
/// machinery for the <em>other</em> queue/direction (<c>scrape-requests</c>, API → Worker) — but the SSE
/// routing config (the worker's <c>PublishMessage&lt;LiveUpdate&gt;().ToPostgresqlQueue(...)</c> egress and
/// the API's <c>ListenToPostgresqlQueue(...)</c> ingress in <see cref="WolverineConfig"/>) was otherwise
/// only exercised in production. A wrong queue name or a missing listener would silently kill realtime
/// updates in the Docker split deployment exactly like the original double-prefix bug did — embedded mode
/// (dev / Pi) handles <see cref="LiveUpdate"/> in-process and would never reveal it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SseCrossProcessDeliveryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task LiveUpdate_FromSplitWorker_CrossesPostgresTransport_AndFansOutToSseStream()
    {
        var conn = fixture.ConnectionString;
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // The real registry, shared into the API host's DI so PushLiveUpdateHandler fans out to the
        // connection we open below — exactly what the StreamEvents endpoint resolves at runtime.
        var registry = new SseConnectionRegistry();

        using var api = BuildApiHost(conn, registry);
        using var worker = BuildWorkerHost(conn);

        // Hosts starting without throwing already proves the SSE routing isn't ambiguous/misconfigured.
        await api.StartAsync(ct);
        await worker.StartAsync(ct);

        // A browser subscribed: open an SSE connection for the user on this API instance.
        var connection = registry.Register(userId);

        // The worker emits a LiveUpdate (as CheckProductPriceHandler / ScrapeNewProductHandler do on a
        // completed scrape). In split mode this must route OUT to the Postgres queue, not be handled
        // locally (the worker doesn't host any SSE streams).
        await worker.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new LiveUpdate(userId, LiveUpdate.ScrapeCompleted, productId));

        var frame = await ReadFrameAsync(connection.Reader, TimeSpan.FromSeconds(30), ct);

        await worker.StopAsync(ct);
        await api.StopAsync(ct);

        frame.Should().NotBeNull(
            "the LiveUpdate published by the split worker must be delivered over the Postgres transport "
            + "to the API and fanned out to the user's open SSE stream");
        frame.Should().Contain(LiveUpdate.ScrapeCompleted, "the frame carries the update kind");
        frame.Should().Contain(productId.ToString(), "the frame carries the affected product id");
    }

    [Fact]
    public async Task LiveUpdate_OverTransport_OnlyReachesTheTargetUsersStream()
    {
        // The fan-out is per-user (LiveUpdate.UserId), so a different user's open stream must NOT receive
        // another user's ping even though both ride the same shared queue.
        var conn = fixture.ConnectionString;
        var ct = TestContext.Current.CancellationToken;
        var targetUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var registry = new SseConnectionRegistry();
        using var api = BuildApiHost(conn, registry);
        using var worker = BuildWorkerHost(conn);

        await api.StartAsync(ct);
        await worker.StartAsync(ct);

        var targetConn = registry.Register(targetUser);
        var otherConn = registry.Register(otherUser);

        await worker.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new LiveUpdate(targetUser, LiveUpdate.ScrapeCompleted, productId));

        var targetFrame = await ReadFrameAsync(targetConn.Reader, TimeSpan.FromSeconds(30), ct);
        // Short wait: the other user must still have nothing after the target has been served.
        var otherFrame = await ReadFrameAsync(otherConn.Reader, TimeSpan.FromMilliseconds(500), ct);

        await worker.StopAsync(ct);
        await api.StopAsync(ct);

        targetFrame.Should().NotBeNull("the addressed user's stream receives the ping");
        otherFrame.Should().BeNull("a different user's stream must not receive another user's update");
    }

    private static async Task<string?> ReadFrameAsync(ChannelReader<string> reader, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out waiting for a frame — treat as "nothing arrived".
            return null;
        }
    }

    private static IHost BuildApiHost(string conn, ISseConnectionRegistry registry)
    {
        var builder = BaseBuilder(conn);

        // The SSE fan-out registry the handler resolves; share the test's instance so we can observe it.
        builder.Services.AddSingleton(registry);

        builder.Services.AddWolverine(opts =>
        {
            WolverineConfig.Configure(opts, WolverineMode.SplitApi, conn);
            // The real API discovers PushLiveUpdateHandler via its entry-assembly scan; here the entry
            // assembly is the test project, so include the handler explicitly.
            opts.Discovery.IncludeType(typeof(PushLiveUpdateHandler));
        });
        return builder.Build();
    }

    private static IHost BuildWorkerHost(string conn)
    {
        var builder = BaseBuilder(conn);
        // No real scraping: if a stray scrape-requests envelope is delivered, keep it inert/offline.
        builder.Services.RemoveAll<IScrapingService>();
        builder.Services.AddScoped<IScrapingService>(_ => new NoOpScrapingService());

        builder.Services.AddWolverine(opts => WolverineConfig.Configure(opts, WolverineMode.SplitWorker, conn));
        return builder.Build();
    }

    private static HostApplicationBuilder BaseBuilder(string conn)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = conn,
            ["DB_PROVIDER"] = "postgres",
            ["DISABLE_PLAYWRIGHT"] = "true",
        });
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        return builder;
    }

    private sealed class NoOpScrapingService : IScrapingService
    {
        public Task<ScrapingResult> ScrapeProductAsync(
            string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
            => Task.FromResult(ScrapingResult.Failure("no-op (SSE test)"));

        public Task<ScrapingResult> ScrapeWithConfigAsync(
            string url, StoreConfig config, CancellationToken cancellationToken = default)
            => ScrapeProductAsync(url, cancellationToken: cancellationToken);
    }
}
