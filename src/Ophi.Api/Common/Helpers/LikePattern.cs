namespace Ophi.Api.Common.Helpers;

/// <summary>
/// Builds a case-insensitive "contains" LIKE pattern from a user-supplied search term.
/// LIKE metacharacters (<c>%</c>, <c>_</c>) and the escape character itself (<c>\</c>) are
/// escaped so they match literally — otherwise a search for <c>50%</c> would match everything
/// containing <c>50</c>, and <c>a_b</c> would match <c>axb</c>.
///
/// Pair with the three-argument <c>EF.Functions.Like(col.ToLower(), pattern, LikePattern.EscapeChar)</c>,
/// which translates to <c>LIKE @pattern ESCAPE '\'</c> on both SQLite and Npgsql.
/// </summary>
public static class LikePattern
{
    /// <summary>The escape character to pass as the third argument of <c>EF.Functions.Like</c>.</summary>
    public const string EscapeChar = "\\";

    /// <summary>
    /// Lower-cases the term and escapes LIKE metacharacters, then wraps it in <c>%…%</c> for a
    /// case-insensitive substring match. The caller decides whether to trim first.
    /// </summary>
    public static string Contains(string term)
    {
        // Escape the backslash first so we don't double-escape the escapes we add next.
        var escaped = term
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escaped.ToLowerInvariant()}%";
    }
}
