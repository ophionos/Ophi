using System.Globalization;
using System.Text.RegularExpressions;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Shared utility for locale-aware price parsing.
/// Determines the decimal separator from the locale's CultureInfo, then normalizes
/// the numeric string to invariant format for reliable parsing.
/// When the locale is ambiguous (e.g., en-US default), applies heuristics to detect
/// European-style comma-decimal formats.
/// </summary>
public static partial class PriceParser
{
    /// <summary>
    /// Parses a price string using the specified locale for number formatting.
    /// </summary>
    /// <param name="priceText">Raw price text (e.g., "1.299,99 €", "$649.00")</param>
    /// <param name="priceLocale">BCP 47 locale string (e.g., "pt-PT", "en-US"). Defaults to "en-US".</param>
    /// <returns>Parsed amount and detected currency, or null if parsing fails.</returns>
    public static (decimal Amount, string Currency)? Parse(string? priceText, string? priceLocale = "en-US")
    {
        if (string.IsNullOrWhiteSpace(priceText))
            return null;

        priceText = priceText.Trim();

        // Detect currency from symbols
        var currency = DetectCurrency(priceText);

        // Strip currency symbols, keep only digits, commas, periods
        var cleaned = StripCurrencySymbols(priceText);

        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        // Try to auto-detect the format from the string itself before falling back to locale
        var detectedFormat = DetectFormat(cleaned);

        if (detectedFormat != NumberFormat.Ambiguous)
        {
            var normalized = NormalizeWithFormat(cleaned, detectedFormat);
            if (decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var detected))
            {
                return AsPrice(detected, currency);
            }
        }

        // Fall back to locale-based parsing
        var culture = GetCulture(priceLocale);
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;

        var localNormalized = NormalizeWithFormat(cleaned,
            decimalSeparator == "," ? NumberFormat.European : NumberFormat.UsUk);

        if (decimal.TryParse(localNormalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount))
        {
            return AsPrice(amount, currency);
        }

        return null;
    }

    /// <summary>
    /// Gates a successfully-parsed number on actually being a price. Scraped pages routinely yield
    /// savings/discount elements ("-$15.00") and placeholders ("$0.00") that parse cleanly but are
    /// not prices — the strip regex deliberately keeps the ASCII hyphen, so a leading minus survives
    /// into <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>.
    /// Returning null makes every caller treat it as "this selector produced nothing" and fall
    /// through to the next selector/JSONPath/regex, instead of recording a non-positive price that
    /// would win the cross-URL MIN in <c>ProductPriceAggregator</c> and fire every "below X" alert.
    /// </summary>
    private static (decimal Amount, string Currency)? AsPrice(decimal amount, string currency) =>
        amount > 0 ? (amount, currency) : null;

    private enum NumberFormat { UsUk, European, Ambiguous }

    /// <summary>
    /// Detects number format from the string structure when unambiguous.
    /// </summary>
    private static NumberFormat DetectFormat(string cleaned)
    {
        var hasComma = cleaned.Contains(',');
        var hasPeriod = cleaned.Contains('.');

        if (hasComma && hasPeriod)
        {
            // Both present: the last separator is the decimal separator
            var lastComma = cleaned.LastIndexOf(',');
            var lastPeriod = cleaned.LastIndexOf('.');
            return lastComma > lastPeriod ? NumberFormat.European : NumberFormat.UsUk;
        }

        if (hasComma && !hasPeriod)
        {
            // Only commas: check if it looks like a decimal separator
            // "499,90" or "1499,99" → comma is decimal (1-2 trailing digits)
            // "1,299" → comma is thousands (3 trailing digits)
            var commaMatch = TrailingCommaPattern().Match(cleaned);
            if (commaMatch.Success)
            {
                var afterComma = commaMatch.Groups[1].Value;
                return afterComma.Length <= 2 ? NumberFormat.European : NumberFormat.UsUk;
            }
        }

        if (hasPeriod && !hasComma)
        {
            // Only periods: apply same heuristic as commas
            // "3.59" or "649.00" → period is decimal (1-2 trailing digits)
            // "1.299" → period could be thousands (3 trailing digits) — ambiguous
            var periodMatch = TrailingPeriodPattern().Match(cleaned);
            if (periodMatch.Success)
            {
                var afterPeriod = periodMatch.Groups[1].Value;
                if (afterPeriod.Length <= 2)
                    return NumberFormat.UsUk;
            }
        }

        // No separators or ambiguous — defer to locale
        return NumberFormat.Ambiguous;
    }

    private static string NormalizeWithFormat(string cleaned, NumberFormat format)
    {
        if (format == NumberFormat.European)
        {
            return cleaned
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace(".", "")
                .Replace(",", ".");
        }

        // US/UK
        return cleaned
            .Replace(" ", "")
            .Replace("\u00A0", "")
            .Replace(",", "");
    }

    private static string DetectCurrency(string priceText) =>
        priceText.Contains('£') ? "GBP" :
        priceText.Contains('€') ? "EUR" :
        priceText.Contains('¥') ? "JPY" : "USD";

    private static string StripCurrencySymbols(string priceText) => CurrencySymbolRegex().Replace(priceText, "").Trim();

    private static CultureInfo GetCulture(string? priceLocale)
    {
        if (string.IsNullOrEmpty(priceLocale))
            return CultureInfo.GetCultureInfo("en-US");

        try
        {
            return CultureInfo.GetCultureInfo(priceLocale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    [GeneratedRegex(@"[^\d.,\s\u00A0-]")]
    private static partial Regex CurrencySymbolRegex();

    /// <summary>
    /// Matches the portion after the last comma: e.g., "499,90" captures "90".
    /// </summary>
    [GeneratedRegex(@",(\d+)$")]
    private static partial Regex TrailingCommaPattern();

    /// <summary>
    /// Matches the portion after the last period: e.g., "3.59" captures "59".
    /// </summary>
    [GeneratedRegex(@"\.(\d+)$")]
    private static partial Regex TrailingPeriodPattern();
}
