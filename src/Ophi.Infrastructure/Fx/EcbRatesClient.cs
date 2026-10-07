using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Ophi.Infrastructure.Fx;

public record FxSnapshot(DateTime AsOf, IReadOnlyDictionary<string, decimal> UnitsPerEur);

/// <summary>
/// Fetches the ECB euro reference rates. Feed shape (checked against the live file):
/// <c>&lt;Cube&gt;&lt;Cube time='YYYY-MM-DD'&gt;&lt;Cube currency='USD' rate='1.1367'/&gt;…</c> in the
/// eurofxref namespace, rates as units per 1 EUR.
/// </summary>
public class EcbRatesClient(HttpClient httpClient, ILogger<EcbRatesClient> logger)
{
    public const string FeedUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    private static readonly XNamespace Ns = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    public async Task<FxSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var xml = await httpClient.GetStringAsync(FeedUrl, cancellationToken);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new FormatException("ECB feed is not valid XML", ex);
        }

        var day = doc.Descendants(Ns + "Cube").FirstOrDefault(c => c.Attribute("time") != null)
            ?? throw new FormatException("ECB feed has no dated Cube");

        var asOf = DateTime.SpecifyKind(
            DateTime.ParseExact((string)day.Attribute("time")!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeKind.Utc);

        var rates = day.Elements(Ns + "Cube")
            .Where(c => c.Attribute("currency") != null && c.Attribute("rate") != null)
            .ToDictionary(
                c => ((string)c.Attribute("currency")!).ToUpperInvariant(),
                c => decimal.Parse((string)c.Attribute("rate")!, NumberStyles.Number, CultureInfo.InvariantCulture));

        if (rates.Count == 0) throw new FormatException("ECB feed has no rates");

        logger.LogDebug("Fetched {Count} ECB rates as of {AsOf:yyyy-MM-dd}", rates.Count, asOf);
        return new FxSnapshot(asOf, rates);
    }
}
