namespace Ophi.Infrastructure.Scraping.Adapters.StoreConfigs;

/// <summary>
/// Store configuration for Amazon (all regional domains).
/// </summary>
public static class AmazonConfig
{
    public static StoreConfig Create() => new()
    {
        Id = "amazon",
        Name = "Amazon",
        RequiresJavaScript = true, // Amazon heavily relies on JavaScript rendering
        DomainPatterns =
        [
            "amazon.com",
            "amazon.co.uk",
            "amazon.de",
            "amazon.fr",
            "amazon.es",
            "amazon.it",
            "amazon.ca",
            "amazon.com.au",
            "amazon.co.jp",
            "amazon.in",
            "amazon.com.mx",
            "amazon.com.br",
            "amazon.nl",
            "amazon.se",
            "amazon.pl",
            "amazon.sg"
        ],
        Selectors = new StoreSelectorConfig
        {
            PriceSelectors =
            [
                // Amazon-specific selectors (highest priority)
                ".a-price .a-offscreen",
                "#priceblock_ourprice",
                "#priceblock_dealprice",
                "#priceblock_saleprice",
                ".a-price-whole",
                "#corePrice_feature_div .a-offscreen",
                "#corePriceDisplay_desktop_feature_div .a-offscreen",
                "#apex_desktop .a-offscreen",
                // Structured data fallback
                "meta[property='product:price:amount']|content",
                "[itemprop='price']|content"
            ],
            NameSelectors =
            [
                "#productTitle",
                "#title",
                "meta[property='og:title']|content"
            ],
            ImageSelectors =
            [
                "#landingImage|src",
                "#imgBlkFront|src",
                "#main-image|src",
                "#ebooksImgBlkFront|src",
                "meta[property='og:image']|content"
            ],
            PriceRegexPatterns =
            [
                @"""priceAmount""\s?:\s?(\d+\.?\d*)",
                @"""price""\s?:\s?""([^""]+)"""
            ],
            ImageRegexPatterns =
            [
                @"""hiRes""\s?:\s?""([^""]+)""",
                @"""large""\s?:\s?""([^""]+)"""
            ],
            OutOfStockSelectors =
            [
                "#availability",
                "#outOfStock",
                "#availabilityInsideBuyBox_feature_div"
            ],
            OutOfStockTextPatterns =
            [
                "currently unavailable",
                "out of stock",
                "not available"
            ]
        }
    };
}
