namespace Ophi.Infrastructure.Scraping.Adapters.StoreConfigs;

/// <summary>
/// Store configuration for eBay (all regional domains).
/// </summary>
public static class EbayConfig
{
    public static StoreConfig Create() => new()
    {
        Id = "ebay",
        Name = "eBay",
        DomainPatterns =
        [
            "ebay.com",
            "ebay.co.uk",
            "ebay.de",
            "ebay.fr",
            "ebay.es",
            "ebay.it",
            "ebay.ca",
            "ebay.com.au",
            "ebay.at",
            "ebay.ch",
            "ebay.ie",
            "ebay.nl",
            "ebay.be",
            "ebay.pl"
        ],
        Selectors = new StoreSelectorConfig
        {
            PriceSelectors =
            [
                // Structured data first — machine-readable, locale-independent
                "[itemprop='price']|content",
                "meta[property='product:price:amount']|content",
                // eBay-specific display selectors
                ".x-price-primary .ux-textspans",
                "[data-testid='x-price-primary'] .ux-textspans",
                ".x-bin-price .ux-textspans",
                "#prcIsum",
                "#mm-saleDscPrc",
                ".vi-price .notranslate"
            ],
            NameSelectors =
            [
                ".x-item-title__mainTitle .ux-textspans",
                "[data-testid='x-item-title'] .ux-textspans",
                "#itemTitle",
                "h1.it-ttl",
                "meta[property='og:title']|content"
            ],
            ImageSelectors =
            [
                // Modern eBay image selectors
                ".ux-image-carousel-item img|src",
                "[data-testid='ux-image-carousel'] img|src",
                ".image-treatment img|src",
                // Legacy selectors
                ".ux-image-magnify__image--original|src",
                "#icImg|src",
                ".img-wrapper img|src",
                // Fallback to meta
                "meta[property='og:image']|content"
            ],
            PriceRegexPatterns =
            [
                @"""price""\s?:\s?""([^""]+)""",
                @"""convertedCurrentPrice""\s?:\s?\{[^}]*""value""\s?:\s?""?(\d+\.?\d*)""?"
            ],
            ImageRegexPatterns =
            [
                @"""image""\s?:\s?""([^""]+)"""
            ],
            OutOfStockSelectors =
            [
                ".d-quantity__availability",
                ".vi-qty-rev-msg"
            ],
            OutOfStockTextPatterns =
            [
                "out of stock",
                "no longer available",
                "this listing has ended"
            ]
        }
    };
}
