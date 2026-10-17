using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ophi.Infrastructure.Persistence;

/// <summary>
/// Stores a <c>List&lt;string&gt;</c> as one comma-joined column (<c>ApiKey.Scopes</c>,
/// <c>WebhookTarget.Events</c>).
///
/// The value comparer is the important part. Without it EF compares the list by reference, so an
/// in-place mutation (<c>target.Events.Add(...)</c>) was never detected and silently not saved, and
/// EF logged a model-validation warning (10620) at every startup. The snapshot copies the list, so
/// the original values do not change with the tracked instance.
/// </summary>
public static class StringListConversion
{
    private static readonly ValueConverter<List<string>, string> Converter = new(
        v => string.Join(',', v),
        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

    private static readonly ValueComparer<List<string>> Comparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v.ToList());

    public static PropertyBuilder<List<string>> HasCommaSeparatedConversion(
        this PropertyBuilder<List<string>> property) =>
        property.HasConversion(Converter, Comparer);
}
