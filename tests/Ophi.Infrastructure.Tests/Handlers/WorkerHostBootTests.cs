using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Worker.Configuration;
using Wolverine;
using Wolverine.Runtime;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// Boots the worker's Wolverine host and forces every discovered handler to generate and compile its
/// executor.
///
/// <para>
/// This closes the gap recorded in docs/agent-notes.md § Messaging &amp; alerts: the worker host was
/// only ever boot-verified by hand. The failure mode it guards is specific and invisible to unit
/// tests — Wolverine 6 codegen builds an executor per handler at runtime, and our handlers inject the
/// scoped <c>OphiDbContext</c>, which codegen cannot inline-construct. That is why
/// <see cref="WolverineConfig"/> sets <c>ServiceLocationPolicy.AlwaysAllowed</c>. Remove that line, or
/// add a handler taking a dependency Wolverine can neither inline nor service-locate, and every unit
/// test still passes: handler methods are plain static methods that tests call directly. The process
/// only fails when Wolverine bootstraps it for real.
/// </para>
///
/// <para>
/// Runs without Postgres on purpose, so it sits in the default tier and gates every commit rather than
/// the Postgres-only one. Absent a connection string <see cref="WolverineConfig"/> skips the durable
/// transport, which is the only part this drops — handler discovery, queue policy and codegen, the
/// fragile parts, all run identically. <c>Ophi.Postgres.Tests/SseCrossProcessDeliveryTests</c> covers
/// the durable transport separately, but only for the one message it drives.
/// </para>
/// </summary>
public class WorkerHostBootTests : IDisposable
{
    /// <summary>
    /// Every SQLite file this test instance asked for, so <see cref="Dispose"/> can remove them.
    /// xUnit builds one instance per test, so this only ever holds the current test's paths.
    /// </summary>
    private readonly List<string> _databasePaths = [];

    /// <summary>
    /// Every message the worker is responsible for. Asserted explicitly rather than by counting
    /// chains: a count passes when a handler is silently swapped for another, and the point here is
    /// that each of these still resolves.
    /// </summary>
    public static TheoryData<Type> WorkerMessages() =>
    [
        typeof(ScrapeProductUrlCommand),
        typeof(PriceUpdatedEvent),
        typeof(AlertTriggeredEvent),
        typeof(SendAlertEmailRequested),
        typeof(SendAlertDiscordRequested),
        typeof(SendAlertWebhookRequested),
    ];

    [Fact]
    public async Task WorkerHost_Boots()
    {
        using var host = BuildWorkerHost();

        var act = async () => await host.StartAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync(
            "Wolverine bootstrap is where a bad messaging composition surfaces — nothing else in the "
            + "suite starts this host");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(WorkerMessages))]
    public async Task WorkerHost_CompilesTheExecutorFor(Type messageType)
    {
        // Bootstrapping alone does not prove codegen works: Wolverine builds each executor lazily, so a
        // handler whose dependencies it cannot construct only throws when the first message of that
        // type arrives — in production, at 3am, on the scrape path. Asking for the invoker forces that
        // work now, and a null answer means the handler was never discovered at all.
        using var host = BuildWorkerHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();

        var act = () => runtime.FindInvoker(messageType);

        act.Should().NotThrow(
            $"Wolverine must be able to generate and compile an executor for {messageType.Name}");
        IsRealHandler(act()).Should().BeTrue($"{messageType.Name} must be handled by the worker");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkerHost_DoesNotHandleLiveUpdate_ItRoutesItToTheApi()
    {
        // The negative control for the theory above — without it, a FindInvoker that answered
        // non-null for anything would make every case pass vacuously.
        // It is also a real invariant: the SSE fan-out handler lives in the Ophi.Api assembly, which
        // this process does not discover. WolverineConfig publishes LiveUpdate to the notifications
        // queue instead, which is what stops the worker trying to fan out to streams it doesn't host.
        using var host = BuildWorkerHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();

        IsRealHandler(runtime.FindInvoker(typeof(LiveUpdate))).Should().BeFalse(
            "the worker publishes LiveUpdate to the API rather than handling it");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <c>FindInvoker</c> does not return null for an unhandled message — it returns Wolverine's
    /// <c>NoHandlerExecutor</c> sentinel, which swallows the message. Asserting only "not null" would
    /// therefore pass with no handlers registered at all, which is how this test was wrong before the
    /// LiveUpdate control caught it. Matched by name because the sentinel is internal to Wolverine.
    /// </summary>
    private static bool IsRealHandler(object? invoker) =>
        invoker is not null && invoker.GetType().Name != "NoHandlerExecutor";

    /// <summary>
    /// Mirrors the composition in <c>Ophi.Worker/Program.cs</c>: infrastructure, settings binding and
    /// Wolverine in <see cref="WolverineMode.SplitWorker"/>. The hosted services
    /// (<c>PriceCheckDispatcher</c>) are deliberately left out — they start timers and would make this
    /// a scheduling test; what is under test is the messaging composition.
    /// </summary>
    private IHost BuildWorkerHost()
    {
        // Each run gets its own file: AddInfrastructure opens the database during registration to
        // set WAL, so a shared path makes concurrent test classes contend on it. Recorded so
        // Dispose can delete it — eight hosts boot per run of this class, and left alone they
        // accumulate in %TEMP% on every developer machine that runs the suite.
        var databasePath = Path.Combine(Path.GetTempPath(), $"ophi-boot-{Guid.NewGuid():N}.db");
        _databasePaths.Add(databasePath);

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_PROVIDER"] = "sqlite",
            ["DATABASE_PATH"] = databasePath,
            ["DISABLE_PLAYWRIGHT"] = "true",
        });

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);

        // No real scraping — this test boots the host, it does not exercise the network.
        builder.Services.RemoveAll<IScrapingService>();
        builder.Services.AddScoped<IScrapingService>(_ => new UnusedScrapingService());

        // No connection string: durable transport is skipped, handler discovery and codegen are not.
        builder.Services.AddWolverine(opts =>
            WolverineConfig.Configure(opts, WolverineMode.SplitWorker, postgresConnectionString: null));

        return builder.Build();
    }

    /// <summary>
    /// Removes the SQLite files the booted hosts created.
    ///
    /// <para>
    /// <c>ClearAllPools</c> first, mirroring <c>Ophi.Postgres.Tests/PostgresFixture</c>: the WAL pragma
    /// in <c>AddInfrastructure</c> opens a pooled <see cref="SqliteConnection"/> and disposing it
    /// returns the handle to the pool rather than closing the file, so a bare Delete loses the race.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var databasePath in _databasePaths)
        {
            // -wal and -shm are normally removed when the last connection closes cleanly; delete them
            // defensively so a run killed mid-test doesn't leave siblings behind either.
            TryDelete(databasePath);
            TryDelete($"{databasePath}-wal");
            TryDelete($"{databasePath}-shm");
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Best-effort delete. Cleanup must never fail an otherwise-passing test — a leftover file is a
    /// tidiness problem, a spurious red build is a real one.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Still held open — nothing useful to do from here.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class UnusedScrapingService : IScrapingService
    {
        public Task<ScrapingResult> ScrapeProductAsync(
            string url, string? selector = null, Guid? userId = null, bool captureHtml = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ScrapingResult.Failure("no-op (host boot test)"));

        public Task<ScrapingResult> ScrapeWithConfigAsync(
            string url, StoreConfig config, CancellationToken cancellationToken = default)
            => ScrapeProductAsync(url, cancellationToken: cancellationToken);
    }
}
