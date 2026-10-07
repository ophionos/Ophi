namespace Ophi.Domain.Extensions;

public static class EnumExtensions
{
    extension(Enum value)
    {
        // camelCase: lowercases the first letter, preserves the rest (e.g. PercentDrop -> percentDrop, PriceAlert -> priceAlert).
        public string ToApiString()
        {
            var name = value.ToString();
            return name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;
        }
    }
}
