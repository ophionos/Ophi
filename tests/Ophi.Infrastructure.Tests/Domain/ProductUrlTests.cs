using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Tests.Domain;

public class ProductUrlTests
{
    private static readonly DateTime Now = new(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordSuccessfulScrape_StoresPriceCurrencyAndClearsErrorState()
    {
        var url = new ProductUrl
        {
            Currency = "USD",
            FailureCount = 4,
            LastError = "previous failure",
            IsOutOfStock = true
        };

        url.RecordSuccessfulScrape(99.99m, "EUR", Now);

        url.CurrentPrice.Should().Be(99.99m);
        url.Currency.Should().Be("EUR");
        url.LastCheckedAt.Should().Be(Now);
        url.LastError.Should().BeNull();
        url.FailureCount.Should().Be(0);
        url.IsOutOfStock.Should().BeFalse();
    }

    [Fact]
    public void RecordSuccessfulScrape_KeepsExistingCurrencyWhenResultCurrencyIsNull()
    {
        var url = new ProductUrl { Currency = "GBP" };

        url.RecordSuccessfulScrape(50m, null, Now);

        url.Currency.Should().Be("GBP");
    }

    [Fact]
    public void RecordSuccessfulScrape_DoesNotTouchSuspiciousState()
    {
        // Suspicious state is managed separately so the handler's health-analyzer logic stays
        // authoritative. A successful scrape with a suspicious URL keeps the marks until the
        // caller decides whether to ClearSuspicious().
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Suspicious,
            SuspiciousCount = 2,
            SuspiciousReason = "redirected"
        };

        url.RecordSuccessfulScrape(10m, "USD", Now);

        url.Status.Should().Be(ProductUrlStatus.Suspicious);
        url.SuspiciousCount.Should().Be(2);
        url.SuspiciousReason.Should().Be("redirected");
    }

    [Fact]
    public void RecordFailure_IncrementsCountAndStoresError()
    {
        var url = new ProductUrl { FailureCount = 2 };

        url.RecordFailure("HTTP 500", Now);

        url.FailureCount.Should().Be(3);
        url.LastError.Should().Be("HTTP 500");
        url.LastCheckedAt.Should().Be(Now);
    }

    [Fact]
    public void RecordTransientFailure_DoesNotIncrementCount()
    {
        // Rate-limited errors are expected to recover; bumping FailureCount would drag the URL
        // toward auto-pause unfairly.
        var url = new ProductUrl { FailureCount = 2 };

        url.RecordTransientFailure("HTTP 429", Now);

        url.FailureCount.Should().Be(2);
        url.LastError.Should().Be("HTTP 429");
        url.LastCheckedAt.Should().Be(Now);
    }

    [Fact]
    public void MarkOutOfStock_FromInStock_ReturnsTrueAndPreservesPrice()
    {
        var url = new ProductUrl
        {
            CurrentPrice = 25m,
            FailureCount = 1,
            LastError = "transient",
            IsOutOfStock = false
        };

        var wasInStock = url.MarkOutOfStock(Now);

        wasInStock.Should().BeTrue();
        url.IsOutOfStock.Should().BeTrue();
        url.CurrentPrice.Should().Be(25m); // Last-known price preserved
        url.FailureCount.Should().Be(0);
        url.LastError.Should().BeNull();
        url.LastCheckedAt.Should().Be(Now);
    }

    [Fact]
    public void MarkOutOfStock_AlreadyOutOfStock_ReturnsFalse()
    {
        // Transition signal — caller uses this to skip publishing a duplicate notification.
        var url = new ProductUrl { IsOutOfStock = true };

        var wasInStock = url.MarkOutOfStock(Now);

        wasInStock.Should().BeFalse();
    }

    [Fact]
    public void MarkOutOfStock_WithoutAPrice_AdoptsTheScrapedCurrency()
    {
        var url = new ProductUrl { Currency = "USD", CurrentPrice = null };

        url.MarkOutOfStock(Now, "EUR");

        url.Currency.Should().Be("EUR");
    }

    [Fact]
    public void MarkOutOfStock_WithAPrice_KeepsTheCurrencyOfThatPrice()
    {
        // The last-known price is preserved, so its denomination must be too: relabelling USD 25
        // as EUR 25 would feed a wrong amount into the next aggregate.
        var url = new ProductUrl { Currency = "USD", CurrentPrice = 25m };

        url.MarkOutOfStock(Now, "EUR");

        url.Currency.Should().Be("USD");
        url.CurrentPrice.Should().Be(25m);
    }

    [Fact]
    public void MarkSuspicious_OnPausedUrl_StaysPaused()
    {
        // Mirrors ClearSuspicious: only Resume() leaves Paused. A suspicious scrape of a paused URL
        // must not un-pause it by flipping Status to Suspicious.
        var url = new ProductUrl { Status = ProductUrlStatus.Paused, SuspiciousCount = 3 };

        url.MarkSuspicious("redirected");

        url.Status.Should().Be(ProductUrlStatus.Paused);
        url.SuspiciousCount.Should().Be(4);
    }

    [Fact]
    public void MarkSuspicious_IncrementsCountAndFlipsStatus()
    {
        var url = new ProductUrl { Status = ProductUrlStatus.Active, SuspiciousCount = 0 };

        url.MarkSuspicious("possible soft-404");

        url.SuspiciousCount.Should().Be(1);
        url.SuspiciousReason.Should().Be("possible soft-404");
        url.Status.Should().Be(ProductUrlStatus.Suspicious);
    }

    [Fact]
    public void MarkSuspicious_AccumulatesAcrossCalls()
    {
        var url = new ProductUrl { SuspiciousCount = 1, SuspiciousReason = "old reason" };

        url.MarkSuspicious("new reason");

        url.SuspiciousCount.Should().Be(2);
        url.SuspiciousReason.Should().Be("new reason");
    }

    [Fact]
    public void ClearSuspicious_ResetsStateOnActiveUrl()
    {
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Suspicious,
            SuspiciousCount = 2,
            SuspiciousReason = "reason"
        };

        url.ClearSuspicious();

        url.SuspiciousCount.Should().Be(0);
        url.SuspiciousReason.Should().BeNull();
        url.Status.Should().Be(ProductUrlStatus.Active);
    }

    [Fact]
    public void ClearSuspicious_OnPausedUrl_DoesNotResume()
    {
        // A paused URL stays paused even after a clean scrape — the user must resume explicitly.
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Paused,
            SuspiciousCount = 3,
            SuspiciousReason = "auto-paused"
        };

        url.ClearSuspicious();

        url.Status.Should().Be(ProductUrlStatus.Paused);
        url.SuspiciousCount.Should().Be(3); // Untouched
    }

    [Fact]
    public void Pause_SetsStatusPaused()
    {
        var url = new ProductUrl { Status = ProductUrlStatus.Active };

        url.Pause();

        url.Status.Should().Be(ProductUrlStatus.Paused);
    }

    [Fact]
    public void Resume_RestoresActiveAndClearsSuspicious()
    {
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Paused,
            SuspiciousCount = 3,
            SuspiciousReason = "auto-paused"
        };

        url.Resume();

        url.Status.Should().Be(ProductUrlStatus.Active);
        url.SuspiciousCount.Should().Be(0);
        url.SuspiciousReason.Should().BeNull();
    }

    [Fact]
    public void Resume_AlsoZeroesFailureCountAndLastError()
    {
        // User-initiated resume means "I think this is fixed" — the retry budget should reset,
        // otherwise a URL with FailureCount = N-1 trips the auto-pause threshold on its next bad
        // scrape, contradicting the resume intent.
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Paused,
            FailureCount = 4,
            LastError = "HTTP 500"
        };

        url.Resume();

        url.FailureCount.Should().Be(0);
        url.LastError.Should().BeNull();
    }

    [Fact]
    public void RequestImmediateRescrape_MarksDueAndResumes()
    {
        // The manual retry path: the URL must be due now (LastCheckedAt null so the dispatcher's
        // IsDue short-circuits true) and active with a clean budget so a previously paused/errored
        // URL is picked up again.
        var url = new ProductUrl
        {
            Status = ProductUrlStatus.Paused,
            FailureCount = 3,
            LastError = "HTTP 500",
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-1)
        };

        url.RequestImmediateRescrape();

        url.LastCheckedAt.Should().BeNull();
        url.Status.Should().Be(ProductUrlStatus.Active);
        url.FailureCount.Should().Be(0);
        url.LastError.Should().BeNull();
    }
}
