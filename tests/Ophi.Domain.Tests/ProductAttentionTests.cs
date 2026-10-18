using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class ProductAttentionTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);

    private static ProductUrl Healthy() => new() { Id = Guid.NewGuid() };

    private static ProductUrl Failed(ScrapeErrorCategory category, int times = 1)
    {
        var url = new ProductUrl { Id = Guid.NewGuid() };
        for (var i = 0; i < times; i++) url.RecordFailure("failed", Now, category);
        return url;
    }

    private static ProductUrl RateLimited(int times)
    {
        var url = new ProductUrl { Id = Guid.NewGuid() };
        for (var i = 0; i < times; i++) url.RecordTransientFailure("Rate limited", Now);
        return url;
    }

    private static ProductUrl Paused(ProductUrl url)
    {
        url.Pause();
        return url;
    }

    private static ProductUrl Anomalous()
    {
        var url = new ProductUrl { Id = Guid.NewGuid() };
        url.RecordPriceAnomaly(true);
        return url;
    }

    private static ProductUrl OutOfStock()
    {
        var url = new ProductUrl { Id = Guid.NewGuid() };
        url.MarkOutOfStock(Now);
        return url;
    }

    public static TheoryData<string, AttentionKind?> SingleUrlCases => new()
    {
        { "healthy", null },
        { "anti-bot once", AttentionKind.Blocked },
        { "auto-paused after anti-bot", AttentionKind.Blocked },
        { "parse error once", AttentionKind.PriceNotFound },
        { "server error once", null },
        { "server error twice", AttentionKind.Failing },
        { "legacy failure without a category", AttentionKind.Failing },
        { "rate limited three times", null },
        { "anomaly", AttentionKind.PriceAnomaly },
        { "paused anomaly", AttentionKind.Paused },
        { "out of stock", AttentionKind.OutOfStock },
        { "paused by the user", AttentionKind.Paused }
    };

    private static ProductUrl Build(string name) => name switch
    {
        "healthy" => Healthy(),
        "anti-bot once" => Failed(ScrapeErrorCategory.AntiBot),
        "auto-paused after anti-bot" => Paused(Failed(ScrapeErrorCategory.AntiBot, 3)),
        "parse error once" => Failed(ScrapeErrorCategory.ParseError),
        "server error once" => Failed(ScrapeErrorCategory.ServerError),
        "server error twice" => Failed(ScrapeErrorCategory.ServerError, 2),
        // A row written before the category column existed: an error, no category.
        "legacy failure without a category" => new ProductUrl
            { Id = Guid.NewGuid(), LastError = "HTTP 500", ConsecutiveFailures = 3 },
        "rate limited three times" => RateLimited(3),
        "anomaly" => Anomalous(),
        "paused anomaly" => Paused(Anomalous()),
        "out of stock" => OutOfStock(),
        "paused by the user" => Paused(Healthy()),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(SingleUrlCases))]
    public void Classify_ReturnsTheExpectedKind(string name, AttentionKind? expected)
    {
        ProductAttention.Classify(Build(name)).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(SingleUrlCases))]
    public void NeedsAction_AgreesWithClassify(string name, AttentionKind? kind)
    {
        // The SQL predicate restates Classify; this keeps the two from drifting apart.
        var product = new Product { ProductUrls = [Build(name)] };

        var needsAction = ProductAttention.NeedsAction.Compile()(product);

        needsAction.Should().Be(kind is { } k && ProductAttention.IsAction(k), name);
    }

    [Fact]
    public void Worst_PicksTheMostSevereActionAcrossUrls()
    {
        var anomaly = Anomalous();
        var blocked = Failed(ScrapeErrorCategory.AntiBot);
        var product = new Product { ProductUrls = [anomaly, Healthy(), blocked] };

        var worst = ProductAttention.Worst(product.ProductUrls);

        worst.Should().NotBeNull();
        worst!.Value.Kind.Should().Be(AttentionKind.Blocked);
        worst.Value.Url.Should().BeSameAs(blocked);
    }

    [Fact]
    public void Worst_IgnoresInfoStatesWhileAnotherUrlIsHealthy()
    {
        // One store out of stock or paused is not news when another store still tracks the price.
        var product = new Product { ProductUrls = [OutOfStock(), Paused(Healthy()), Healthy()] };

        ProductAttention.Worst(product.ProductUrls).Should().BeNull();
    }

    [Fact]
    public void Worst_ReportsAnInfoStateWhenEveryUrlHasOne()
    {
        var product = new Product { ProductUrls = [Paused(Healthy()), OutOfStock()] };

        ProductAttention.Worst(product.ProductUrls)!.Value.Kind.Should().Be(AttentionKind.OutOfStock);
    }

    [Fact]
    public void Worst_WithNoUrls_ReturnsNull()
    {
        ProductAttention.Worst([]).Should().BeNull();
    }

    [Fact]
    public void IsAction_IsTrueOnlyForStatesTheUserCanFix()
    {
        Enum.GetValues<AttentionKind>().Where(ProductAttention.IsAction).Should().Equal(
            AttentionKind.Blocked, AttentionKind.PriceNotFound, AttentionKind.Failing, AttentionKind.PriceAnomaly);
    }
}
