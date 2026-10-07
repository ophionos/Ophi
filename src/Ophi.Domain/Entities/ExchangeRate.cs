namespace Ophi.Domain.Entities;

/// <summary>
/// One ECB euro reference rate. Display-only data: served to the frontend for the optional
/// display-currency conversion and read nowhere else. Alert checking, price aggregation and
/// comparisons must never consult it — alert targets are denominated on purpose (see
/// docs/agent-notes.md).
/// </summary>
public class ExchangeRate
{
    /// <summary>ISO 4217 code. EUR is stored with <see cref="UnitsPerEur"/> = 1.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How many units of <see cref="Currency"/> one euro buys.</summary>
    public decimal UnitsPerEur { get; set; }

    /// <summary>The ECB publication date the rate belongs to (UTC midnight).</summary>
    public DateTime AsOf { get; set; }

    public DateTime FetchedAt { get; set; }
}
