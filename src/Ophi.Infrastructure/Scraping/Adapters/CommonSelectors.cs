namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Common selectors shared across adapters and used as fallback.
/// These are generic patterns that work across many e-commerce sites.
/// </summary>
public static class CommonSelectors
{
    /// <summary>
    /// A price "selector" that reads the offer price of the JSON-LD <c>Product</c> node(s) instead of
    /// the DOM. It can appear anywhere in a price selector list, so JSON-LD can rank between the
    /// precise structured selectors and the broad CSS patterns. See
    /// <see cref="Scraping.JsonPathExtractor.ExtractProductOfferPrices"/>.
    /// </summary>
    public const string JsonLdProductPrice = "jsonld:product-price";

    /// <summary>
    /// Containers of related, upsell and cross-sell product cards. A config with
    /// <see cref="StoreSelectorConfig.ExcludeRelatedProducts"/> ignores price matches inside them.
    /// </summary>
    public const string RelatedProductContainers = ".related, .upsells, .cross-sells, ul.products";

    /// <summary>
    /// Common price selectors for generic e-commerce sites.
    /// Format: "selector" or "selector|attribute" to extract from specific attribute.
    /// </summary>
    public static readonly string[] PriceSelectors =
    [
        // Structured data / Open Graph meta tags (highest priority)
        "meta[property='product:price:amount']|content",
        "meta[property='og:price:amount']|content",
        // Schema.org / structured data
        "[itemprop='price']|content",
        "[itemprop='price']",
        // JSON-LD Product offer — ahead of every broad pattern below, which can match a
        // related-product card before the main price (#136).
        JsonLdProductPrice,
        "[data-price]",
        // WooCommerce: the sale amount, then a lone amount. The whole `.price` text can hold two
        // amounts ("$25.99 (Retail price: $35.99)"), which does not parse.
        ".price ins .woocommerce-Price-amount",
        ".price > .woocommerce-Price-amount",
        // Generic patterns
        ".price",
        ".product-price",
        ".product-price-value",
        ".current-price",
        ".sale-price",
        "#price",
        ".price-current",
        ".price__current",
        // Wildcard class patterns (lower priority)
        "[class^='price']",
        "[class*='price']"
    ];

    /// <summary>
    /// Common name selectors for generic e-commerce sites.
    /// </summary>
    public static readonly string[] NameSelectors =
    [
        // Open Graph meta tag (highest priority)
        "meta[property='og:title']|content",
        // Generic patterns
        "[itemprop='name']",
        "h1.product-title",
        "h1.product-name",
        ".product-title",
        ".product-name",
        "title",
        "h1"
    ];

    /// <summary>
    /// Common image selectors for generic e-commerce sites.
    /// </summary>
    public static readonly string[] ImageSelectors =
    [
        // Open Graph meta tags (highest priority)
        "meta[property='og:image']|content",
        "meta[property='og:image:secure_url']|content",
        // Generic patterns
        "[itemprop='image']",
        ".product-image img",
        ".product-gallery img",
        "#product-image",
        ".main-image img"
    ];

    /// <summary>
    /// Regex patterns for price extraction (fallback when selectors fail).
    /// Stored as strings for serialization compatibility.
    /// </summary>
    public static readonly string[] PriceRegexPatterns =
    [
        @"""price""\s?:\s?""([^""]+)""",    // "price": "99.99" in JSON
        @">\$(\d+(?:\.\d{2})?)<",            // >$99.99< in HTML tags
        @"\$(\d+(?:\.\d{2})?)"               // $99.99 anywhere
    ];

    /// <summary>
    /// Regex patterns for image extraction (fallback when selectors fail).
    /// Stored as strings for serialization compatibility.
    /// </summary>
    public static readonly string[] ImageRegexPatterns =
    [
        @"""image""\s?:\s?""([^""]+\.jpg)""",   // "image": "url.jpg" in JSON
        @"""image""\s?:\s?""([^""]+\.png)"""    // "image": "url.png" in JSON
    ];

    /// <summary>
    /// CSS selectors for elements that indicate out-of-stock status.
    /// The extracted text is compared against <see cref="OutOfStockTextPatterns"/>
    /// and <see cref="OutOfStockSchemaValues"/>.
    /// </summary>
    public static readonly string[] OutOfStockSelectors =
    [
        // Schema.org microdata (highest priority — machine-readable)
        "[itemprop='availability']|content",
        "[itemprop='availability']|href",
        "[itemprop='availability']",
        // Common e-commerce patterns
        "#availability",
        ".availability",
        ".stock-status",
        ".out-of-stock",
        ".sold-out"
    ];

    /// <summary>
    /// Text patterns (case-insensitive) that indicate a product is out of stock
    /// when found within elements matched by <see cref="OutOfStockSelectors"/>.
    /// </summary>
    public static readonly string[] OutOfStockTextPatterns =
    [
        "out of stock",
        "sold out",
        "currently unavailable",
        "not available",
        "no longer available",
        "temporarily out of stock",
        "discontinued"
    ];

    /// <summary>
    /// Text patterns (case-insensitive) indicating the page has NO purchasable offer at all for
    /// the viewer's locale — e.g. an Amazon geo shipping restriction. Unlike
    /// <see cref="OutOfStockTextPatterns"/> (where a last-known price may still be shown and kept),
    /// when one of these matches anywhere on the page body, any extracted price is treated as a
    /// stray value (an unrelated accessory or "compare similar" cell) and discarded: the listing is
    /// recorded as unavailable with no price.
    ///
    /// Keyed on the shipping-restriction phrase, the reliable geo signal. The bare
    /// "no featured offers available" is intentionally excluded — it also appears on contested-buybox
    /// listings that DO have valid third-party offers we want to keep.
    /// </summary>
    public static readonly string[] UnavailableTextPatterns =
    [
        "cannot be shipped to your selected delivery location"
    ];

    /// <summary>
    /// Currency metadata selectors (Open Graph, Schema.org). Shared by HTTP and Playwright services.
    /// </summary>
    public static readonly string[] CurrencySelectors =
    [
        "meta[property='product:price:currency']|content",
        "meta[property='og:price:currency']|content",
        "[itemprop='priceCurrency']|content",
        "[itemprop='priceCurrency']"
    ];

    /// <summary>
    /// Schema.org availability values (case-insensitive) that indicate out-of-stock.
    /// Matched against itemprop="availability" content/href and JSON-LD data.
    /// </summary>
    public static readonly string[] OutOfStockSchemaValues =
    [
        "https://schema.org/OutOfStock",
        "https://schema.org/Discontinued",
        "https://schema.org/SoldOut",
        "http://schema.org/OutOfStock",
        "http://schema.org/Discontinued",
        "http://schema.org/SoldOut",
        "OutOfStock",
        "Discontinued",
        "SoldOut"
    ];
}
