using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ophi.Infrastructure.Persistence;

/// <summary>
/// Forces every <see cref="DateTime"/> to <see cref="DateTimeKind.Utc"/> at the storage boundary.
///
/// Npgsql maps <c>DateTime</c> to <c>timestamp with time zone</c> and throws on write unless the
/// value's <c>Kind</c> is <c>Utc</c>. The app is already UTC-clean (all timestamps come from
/// <c>DateTime.UtcNow</c> / <c>TimeProvider.GetUtcNow().UtcDateTime</c>), so this is belt-and-suspenders:
/// it normalizes any stray <c>Unspecified</c>/<c>Local</c> value so a future code path or a value that
/// lost its Kind on a round-trip can never break a write. Harmless on SQLite (stamps Kind=Utc on read).
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        // to provider: ensure Utc. Unspecified is assumed already-UTC (our convention), not local.
        v => v.Kind == DateTimeKind.Utc
            ? v
            : v.Kind == DateTimeKind.Local
                ? v.ToUniversalTime()
                : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        // from provider: stamp Utc so consumers see Kind=Utc regardless of provider.
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v == null
            ? v
            : v.Value.Kind == DateTimeKind.Utc
                ? v
                : v.Value.Kind == DateTimeKind.Local
                    ? v.Value.ToUniversalTime()
                    : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
    {
    }
}
