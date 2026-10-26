using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Json.Path;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Extracts values from JSON-LD content using JSONPath expressions.
/// Shared between ScrapingService (AngleSharp) and PlaywrightScrapingService.
/// </summary>
internal static class JsonPathExtractor
{
    /// <summary>
    /// Evaluates a single JSONPath expression against a collection of JSON-LD content strings.
    /// Returns the first scalar match, or null if none is found. Shares
    /// <see cref="MatchesInBlock"/> with <see cref="ExtractAll"/> so both apply the same
    /// scalar-only rule and both scan every match in a block rather than only the first.
    /// </summary>
    public static string? Extract(IEnumerable<string> jsonLdContents, string jsonPathExpression)
    {
        if (!JsonPath.TryParse(jsonPathExpression, out var path))
            return null;

        foreach (var content in jsonLdContents)
        {
            var matches = MatchesInBlock(content, path);
            if (matches.Count > 0)
                return matches[0];
        }

        return null;
    }

    /// <summary>
    /// Tries multiple JSONPath expressions in order, returning the first successful match.
    /// </summary>
    public static string? ExtractFirst(IEnumerable<string> jsonLdContents, string[] jsonPaths)
    {
        var contents = jsonLdContents as string[] ?? jsonLdContents.ToArray();

        foreach (var path in jsonPaths)
        {
            var result = Extract(contents, path);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Extracts all JSON-LD text contents from an AngleSharp document.
    /// </summary>
    public static string[] GetJsonLdContents(IDocument document)
    {
        var scripts = document.QuerySelectorAll("script[type='application/ld+json']");
        return scripts
            .Select(s => s.TextContent)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }

    /// <summary>
    /// Enumerates every string-valued match for <paramref name="jsonPathExpression"/> across all
    /// JSON-LD blocks. Use this when the question is "does *any* match satisfy a predicate" rather
    /// than "what is the first match" — see <see cref="ExtractAvailability"/> and
    /// <see cref="ExtractCurrencyCode"/> for usage.
    /// </summary>
    public static IEnumerable<string> ExtractAll(IEnumerable<string> jsonLdContents, string jsonPathExpression)
    {
        if (!JsonPath.TryParse(jsonPathExpression, out var path))
            yield break;

        foreach (var content in jsonLdContents)
        {
            foreach (var match in MatchesInBlock(content, path))
                yield return match;
        }
    }

    private static List<string> MatchesInBlock(string content, JsonPath path)
    {
        try
        {
            var node = JsonNode.Parse(content);
            if (node == null) return [];

            var result = path.Evaluate(node);
            if (result.Matches is not { Count: > 0 }) return [];

            var values = new List<string>(result.Matches.Count);
            foreach (var pm in result.Matches)
            {
                // Only scalars count as a match. A structural node used to be stringified via
                // ToJsonString().Trim('"'), which fed PriceParser things like "[10,20]" — the
                // brackets get stripped and the European-comma heuristic reads the rest as the
                // decimal 10.20, a fabricated price that is neither bound. Every value these paths
                // legitimately target (price, availability, currency, name) is scalar.
                if (pm.Value is not JsonValue jv) continue;
                values.Add(jv.ToString());
            }
            return values;
        }
        catch
        {
            // Malformed JSON or evaluation failure — skip block
            return [];
        }
    }

    /// <summary>
    /// Enumerates the offer prices of every JSON-LD <c>Product</c> node, in document order: at any
    /// depth (so inside <c>@graph</c> or an array), with <c>@type</c> as a string or an array, and
    /// <c>offers</c> as an object or an array. Per offer it takes <c>price</c>, then <c>lowPrice</c>
    /// (AggregateOffer), then <c>priceSpecification.price</c>.
    /// Anchored on the Product node because a plain <c>$..offers.price</c> also matches offers in an
    /// <c>ItemList</c> of related products, which some themes emit ahead of the product (#136).
    /// </summary>
    public static IEnumerable<string> ExtractProductOfferPrices(IEnumerable<string> jsonLdContents)
    {
        foreach (var content in jsonLdContents)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(content);
            }
            catch
            {
                continue;
            }

            var prices = new List<string>();
            CollectProductOfferPrices(root, prices);
            foreach (var price in prices)
                yield return price;
        }
    }

    private static void CollectProductOfferPrices(JsonNode? node, List<string> prices)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                    CollectProductOfferPrices(item, prices);
                break;

            case JsonObject obj when IsProduct(obj["@type"]):
                var offers = obj["offers"];
                // Not `cond ? array : [offers]`: that types the literal as a JsonArray, and adding a
                // node that already has a parent throws.
                IEnumerable<JsonNode?> offerList = offers is JsonArray offerArray ? offerArray : new[] { offers };
                foreach (var offer in offerList)
                {
                    if (offer is not JsonObject offerObj) continue;
                    var price = ScalarOrNull(offerObj["price"])
                        ?? ScalarOrNull(offerObj["lowPrice"])
                        ?? ScalarOrNull(offerObj["priceSpecification"]?["price"]);
                    if (price != null) prices.Add(price);
                }
                break;

            case JsonObject obj:
                foreach (var (_, child) in obj)
                    CollectProductOfferPrices(child, prices);
                break;
        }
    }

    private static bool IsProduct(JsonNode? type) => type switch
    {
        JsonValue value => value.ToString() is "Product" or "IndividualProduct",
        JsonArray array => array.Any(t => t is JsonValue v && v.ToString() is "Product" or "IndividualProduct"),
        _ => false
    };

    // Scalars only, for the same reason as MatchesInBlock.
    private static string? ScalarOrNull(JsonNode? node) =>
        node is JsonValue value ? value.ToString() : null;

    /// <summary>
    /// Enumerates every Schema.org availability value found in JSON-LD blocks (recursive descent).
    /// Returns *all* matches across *all* blocks so callers can flag OOS when any single match
    /// indicates unavailability — mirrors the old per-block regex semantics that this helper replaced.
    /// </summary>
    public static IEnumerable<string> ExtractAvailability(IEnumerable<string> jsonLdContents) =>
        ExtractAll(jsonLdContents, "$..availability");

    /// <summary>
    /// Extracts the ISO 4217 currency code from JSON-LD blocks. Iterates every <c>$..priceCurrency</c>
    /// match across every block and returns the first one that normalizes to a 3-letter code,
    /// so a stray non-conforming value in one block doesn't mask a valid code in a sibling block.
    /// </summary>
    public static string? ExtractCurrencyCode(IEnumerable<string> jsonLdContents)
    {
        foreach (var raw in ExtractAll(jsonLdContents, "$..priceCurrency"))
        {
            var normalized = raw.Trim().ToUpperInvariant();
            if (normalized.Length == 3) return normalized;
        }
        return null;
    }
}
