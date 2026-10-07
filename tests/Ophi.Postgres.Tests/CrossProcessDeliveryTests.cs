using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.TestHelpers;
using Ophi.Worker.Configuration;
using Wolverine;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The whole point of Phase 2: a <c>ScrapeProductUrlCommand</c> published by the split <b>API</b>
/// (no in-process worker) is durably delivered over the Postgres transport to the split <b>Worker</b>
/// and run by the real <see cref="Ophi.Worker.Handlers.ScrapeNewProductHandler"/>. None of the SQLite
/// tiers exercise the transport wiring (it's gated off without a Postgres connection), so this is the
/// only coverage for the deploy path — and the arbiter of the <c>[LocalQueue("scraping")]</c>-vs-listener
/// routing question (if routing were ambiguous, the host would fail to start; if the command never
/// reached the handler, the product would stay Pending).
/// </summary>
[Collection("Postgres")]
public class CrossProcessDeliveryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ScrapeProductUrlCommand_FromSplitApi_IsHandledBySplitWorker_OverPostgresTransport()
    {
        var conn = fixture.ConnectionString;
        var ct = TestContext.Current.CancellationToken;

        // Seed a Pending product + URL (what AddProduct creates before publishing the command).
        var userId = Guid.NewGuid();
        Guid productId, urlId;
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId)
                .WithStatus(ProductStatus.Pending).Named("Loading...").Build();
            var url = TestEntityFactory.ProductUrl(product.Id)
                .WithUrl($"https://example.com/{Guid.NewGuid():N}").Build();
            productId = product.Id;
            urlId = url.Id;
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            await ctx.SaveChangesAsync(ct);
        }

        var scraper = new CountingScrapingService();

        using var worker = BuildHost(conn, WolverineMode.SplitWorker, scraper);
        using var api = BuildHost(conn, WolverineMode.SplitApi, scrapingOverride: null);

        // Host startup that doesn't throw already proves the routing isn't ambiguous.
        await worker.StartAsync(ct);
        await api.StartAsync(ct);

        // Publish from the split-API bus → routed out to the Postgres transport queue (not handled here).
        var bus = api.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new ScrapeProductUrlCommand(urlId));

        var becameActive = await WaitForStatusAsync(productId, ProductStatus.Active, TimeSpan.FromSeconds(30), ct);

        await api.StopAsync(ct);
        await worker.StopAsync(ct);

        becameActive.Should().BeTrue(
            "the command published by the split API must be delivered over the Postgres transport and "
            + "handled by the worker's real ScrapeNewProductHandler (Pending → Active)");
        scraper.Count.Should().Be(1, "the handler should have scraped exactly once");
    }

    [Fact]
    public async Task ScrapeProductUrlCommand_InEmbeddedMode_IsHandledInProcess()
    {
        // Embedded mode (ENABLE_WORKER=true) is the single-container deploy config: API + worker in one
        // process, no Postgres listener. The published command must be handled in-process via the local
        // [LocalQueue("scraping")] handler — NOT routed out to the Postgres queue (only SplitApi adds
        // that routing). If it were routed out, nothing would consume it and the product would stay
        // Pending; Pending → Active proves in-process handling with no send-back-out loop.
        var conn = fixture.ConnectionString;
        var ct = TestContext.Current.CancellationToken;

        var userId = Guid.NewGuid();
        Guid productId, urlId;
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId)
                .WithStatus(ProductStatus.Pending).Named("Loading...").Build();
            var url = TestEntityFactory.ProductUrl(product.Id)
                .WithUrl($"https://example.com/{Guid.NewGuid():N}").Build();
            productId = product.Id;
            urlId = url.Id;
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            await ctx.SaveChangesAsync(ct);
        }

        var scraper = new CountingScrapingService();
        using var host = BuildHost(conn, WolverineMode.Embedded, scraper);
        await host.StartAsync(ct);

        var bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new ScrapeProductUrlCommand(urlId));

        var becameActive = await WaitForStatusAsync(productId, ProductStatus.Active, TimeSpan.FromSeconds(30), ct);

        await host.StopAsync(ct);

        becameActive.Should().BeTrue("embedded mode must handle ScrapeProductUrlCommand in-process (Pending → Active)");
        scraper.Count.Should().Be(1, "the handler should have scraped exactly once");
    }

    [Fact]
    public async Task ScrapeProductUrlCommand_PublishedWhileWorkerDown_IsDeliveredWhenWorkerStarts()
    {
        // The core durability payoff over the old poll-bridge: publish with NO worker running, the
        // envelope persists in Postgres, and it is delivered exactly once when the worker comes up.
        var conn = fixture.ConnectionString;
        var ct = TestContext.Current.CancellationToken;

        var userId = Guid.NewGuid();
        Guid productId, urlId;
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId)
                .WithStatus(ProductStatus.Pending).Named("Loading...").Build();
            var url = TestEntityFactory.ProductUrl(product.Id)
                .WithUrl($"https://example.com/{Guid.NewGuid():N}").Build();
            productId = product.Id;
            urlId = url.Id;
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            await ctx.SaveChangesAsync(ct);
        }

        // 1. Publish from a split-API host with NO worker running, then dispose the publisher.
        using (var api = BuildHost(conn, WolverineMode.SplitApi, scrapingOverride: null))
        {
            await api.StartAsync(ct);
            await api.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new ScrapeProductUrlCommand(urlId));
            await api.StopAsync(ct);
        }

        // 2. Nothing consumed it — the product is still Pending, the envelope sits durably in Postgres.
        await using (var ctx = fixture.CreateContext())
        {
            var status = await ctx.Products.Where(p => p.Id == productId).Select(p => p.Status)
                .FirstOrDefaultAsync(ct);
            status.Should().Be(ProductStatus.Pending, "no consumer was running when the command was published");
        }

        // 3. Worker starts later → drains the durable envelope and handles it.
        var scraper = new CountingScrapingService();
        bool becameActive;
        using (var worker = BuildHost(conn, WolverineMode.SplitWorker, scraper))
        {
            await worker.StartAsync(ct);
            becameActive = await WaitForStatusAsync(productId, ProductStatus.Active, TimeSpan.FromSeconds(30), ct);
            await worker.StopAsync(ct);
        }

        becameActive.Should().BeTrue("a command published while the worker was down must be delivered once it starts");
        scraper.Count.Should().Be(1, "the durably-stored command should be handled exactly once");
    }

    private async Task<bool> WaitForStatusAsync(Guid productId, ProductStatus target, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var ctx = fixture.CreateContext();
            var status = await ctx.Products
                .Where(p => p.Id == productId)
                .Select(p => p.Status)
                .FirstOrDefaultAsync(ct);
            if (status == target)
            {
                return true;
            }
            await Task.Delay(250, ct);
        }
        return false;
    }

    private static IHost BuildHost(string conn, WolverineMode mode, IScrapingService? scrapingOverride)
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

        if (scrapingOverride is not null)
        {
            builder.Services.RemoveAll<IScrapingService>();
            builder.Services.AddScoped<IScrapingService>(_ => scrapingOverride);
        }

        builder.Services.AddWolverine(opts => WolverineConfig.Configure(opts, mode, conn));
        return builder.Build();
    }

    private sealed class CountingScrapingService : IScrapingService
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public Task<ScrapingResult> ScrapeProductAsync(
            string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            // StoreId non-generic so the handler skips auto-store-creation (which needs FetchedHtml).
            return Task.FromResult(new ScrapingResult
            {
                Success = true,
                Name = "Scraped Product",
                Price = 42.00m,
                Currency = "USD",
                StoreId = "amazon"
            });
        }

        public Task<ScrapingResult> ScrapeWithConfigAsync(
            string url, StoreConfig config, CancellationToken cancellationToken = default)
            => ScrapeProductAsync(url, cancellationToken: cancellationToken);
    }
}
