using Prometheus;

namespace Ophi.Infrastructure.Metrics;

/// <summary>
/// Centralized Prometheus metric definitions for Ophi.
/// All metrics are static singletons — safe to reference from any handler.
/// </summary>
public static class AppMetrics
{
    // --- Gauges (refreshed by PriceMetricsCollector) ---
    // Price gauges intentionally carry only low-cardinality labels (product_id + store).
    // Product names live on the separate ProductInfo info-metric so dashboards can join via
    //   ophi_product_price_current * on(product_id) group_left(product_name) ophi_product_info
    // This prevents an unbounded label set when products are renamed.

    public static readonly Gauge ProductPriceCurrent = Prometheus.Metrics.CreateGauge(
        "ophi_product_price_current",
        "Current price of a tracked product",
        new GaugeConfiguration { LabelNames = ["product_id", "store"] });

    public static readonly Gauge ProductPriceLowest = Prometheus.Metrics.CreateGauge(
        "ophi_product_price_lowest_alltime",
        "All-time lowest price of a tracked product",
        new GaugeConfiguration { LabelNames = ["product_id", "store"] });

    /// <summary>
    /// Info-metric carrying the product name. Always set to 1; the label set is the payload.
    /// Join with the price gauges via <c>group_left(product_name)</c> in Prometheus queries.
    /// </summary>
    public static readonly Gauge ProductInfo = Prometheus.Metrics.CreateGauge(
        "ophi_product_info",
        "Metadata for a tracked product. Always 1; product_name carried as a label.",
        new GaugeConfiguration { LabelNames = ["product_id", "product_name"] });

    // --- Counters (incremented inline by handlers) ---

    public static readonly Counter ScrapeTotal = Prometheus.Metrics.CreateCounter(
        "ophi_scrape_total",
        "Total number of scrape attempts",
        new CounterConfiguration { LabelNames = ["store", "status"] });

    public static readonly Counter AlertFiredTotal = Prometheus.Metrics.CreateCounter(
        "ophi_alert_fired_total",
        "Total number of alerts fired",
        new CounterConfiguration { LabelNames = ["condition"] });

    public static readonly Counter WebhookDispatchTotal = Prometheus.Metrics.CreateCounter(
        "ophi_webhook_dispatch_total",
        "Total number of webhook dispatches",
        new CounterConfiguration { LabelNames = ["event", "status"] });

    // --- Histogram (recorded inline by handlers) ---

    public static readonly Histogram ScrapeDurationSeconds = Prometheus.Metrics.CreateHistogram(
        "ophi_scrape_duration_seconds",
        "Duration of scrape operations in seconds",
        new HistogramConfiguration
        {
            LabelNames = ["store"],
            Buckets = [0.5, 1, 2, 5, 10, 30, 60],
        });

    /// <summary>
    /// Extract store domain from a URL for use as a Prometheus label.
    /// Returns "unknown" for invalid URLs.
    /// </summary>
    public static string ExtractStore(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return "unknown";

        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return "unknown";
        }
    }
}
