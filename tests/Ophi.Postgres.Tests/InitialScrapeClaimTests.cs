using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Worker.Handlers;
using Wolverine;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Issue #51 on real Postgres: the API publish and the dispatcher's Pending reconciliation can both
/// deliver a first-scrape command for the same URL. The second delivery used to pass the
/// "Status == Pending" guard because Status only leaves Pending when the first scrape commits.
/// The handler's atomic claim must let only one of two in-flight deliveries scrape.
/// </summary>
[Collection("Postgres")]
public class InitialScrapeClaimTests(PostgresFixture fixture)
{
    [Fact]
    public async Task HandleAsync_SecondDeliveryWhileFirstInFlight_ScrapesOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var productUrlId = await SeedPendingProductUrlAsync();

        var firstScrapeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstScrape = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scrapeCalls = 0;

        var scraping = new Mock<IScrapingService>();
        scraping
            .Setup(s => s.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref scrapeCalls) == 1)
                {
                    firstScrapeStarted.SetResult();
                    await releaseFirstScrape.Task;
                }
                return new ScrapingResult { Success = true, Name = "Thing", Price = 10m, Currency = "EUR", StoreId = "test" };
            });

        await using var firstCtx = fixture.CreateContext();
        await using var secondCtx = fixture.CreateContext();

        var first = RunHandlerAsync(firstCtx, scraping.Object, productUrlId, ct);
        await firstScrapeStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // A dispatcher tick lands while the first scrape is still in flight.
        var secondResult = await RunHandlerAsync(secondCtx, scraping.Object, productUrlId, ct)
            .WaitAsync(TimeSpan.FromSeconds(30), ct);

        releaseFirstScrape.SetResult();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(30), ct);

        scrapeCalls.Should().Be(1, "the second delivery must lose the claim instead of scraping again");
        firstResult.Should().NotBeNull();
        secondResult.Should().BeNull("the losing delivery emits no PriceUpdatedEvent");

        await using var verifyCtx = fixture.CreateContext();
        (await verifyCtx.ScrapeLogs.CountAsync(l => l.ProductUrlId == productUrlId, ct)).Should().Be(1);
    }

    private static Task<Ophi.Domain.Messages.Events.PriceUpdatedEvent?> RunHandlerAsync(
        Ophi.Infrastructure.Persistence.OphiDbContext dbContext, IScrapingService scraping,
        Guid productUrlId, CancellationToken ct) =>
        ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrlId), dbContext, scraping,
            Mock.Of<IAutoCreateStoreService>(), Mock.Of<IStoreConfigProvider>(),
            TimeProvider.System, Mock.Of<IMessageBus>(), NullLogger.Instance, ct);

    private async Task<Guid> SeedPendingProductUrlAsync()
    {
        await using var ctx = fixture.CreateContext();
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        ctx.Users.Add(new User
        {
            Id = userId,
            Email = $"claim-{userId:N}@example.com",
            Name = "Claim",
            PasswordHash = "hash"
        });
        ctx.Products.Add(new Product
        {
            Id = productId,
            UserId = userId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        });
        ctx.ProductUrls.Add(new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = $"https://example.com/p/{productUrlId:N}",
            Currency = "USD"
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return productUrlId;
    }
}
