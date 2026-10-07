using System.Globalization;

namespace Ophi.Infrastructure.Formatting;

public static class PriceFormatter
{
    public static string FormatPrice(decimal price) =>
        price.ToString("F2", CultureInfo.InvariantCulture);

    public static string FormatPercent(decimal percent) =>
        percent.ToString("F0", CultureInfo.InvariantCulture);
}
