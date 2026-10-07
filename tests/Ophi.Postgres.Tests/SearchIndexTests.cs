using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Verifies issue #19 on real Postgres against the <em>actual</em> GetProducts/GetTags query shapes —
/// not a hand-simplified proxy. The SQLite unit tier cannot cover this (no pg_trgm, no migrations,
/// different SQL).
///
/// What makes this a real test rather than green-by-construction:
///   * The probed SQL matches EF's emitted form exactly, including the <c>::text</c> cast
///     (<c>lower((col)::text) LIKE @p ESCAPE '\'</c>) and the product search's
///     <c>(lower(Name) LIKE … OR EXISTS(… Url …))</c> structure. An index on <c>lower(col)</c> without
///     the cast would NOT match — that regression is exactly what this guards.
///   * Data is seeded at realistic volume across many users, so <c>UserId</c> is selective like
///     production. That is what makes the user-scoped Tag/Name searches ride their existing UserId
///     indexes, and forces the product search's un-scoped EXISTS subplan onto the Url trigram index
///     instead of a full ProductUrls seq scan.
///   * Seeding ends with VACUUM so each GIN index's fastupdate pending list is flushed — the
///     steady state autovacuum maintains in production, without which a fresh bulk load measures a
///     transient state where the planner avoids the not-yet-merged index.
/// </summary>
[Collection("Postgres")]
public class SearchIndexTests(PostgresFixture fixture)
{
    // The collection runs sequentially, so a static one-shot guard seeds the shared DB once.
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static Guid _targetUser;
    private static bool _seeded;

    [Fact]
    public async Task ProductSearch_UrlSubplan_UsesTrigramIndex_NotSeqScan()
    {
        var user = await SeedOnceAsync();
        var planText = await ExplainAsync(
            // Mirrors GetProducts' emitted SQL: user-scoped, with the OR EXISTS over ProductUrls.
            $"""
             SELECT 1 FROM "Products" p
             WHERE p."UserId" = '{user}'
               AND (lower((p."Name")::text) LIKE '%zzqx%' ESCAPE '\'
                    OR EXISTS (SELECT 1 FROM "ProductUrls" p0
                               WHERE p."Id" = p0."ProductId"
                                 AND lower((p0."Url")::text) LIKE '%zzqx%' ESCAPE '\'))
             """);

        planText.Should().Contain("IX_ProductUrls_Url_trgm",
            $"the un-scoped EXISTS over ProductUrls must use the Url trigram index; plan was:\n{planText}");
        planText.Should().NotContain("Seq Scan on \"ProductUrls\"",
            $"the URL search must not fall back to a full ProductUrls scan; plan was:\n{planText}");
    }

    [Fact]
    public async Task TagSearch_IsIndexServed_NotSeqScan()
    {
        // The tag search is user-scoped, so it rides the existing IX_Tags_UserId_Name composite index
        // (Index Only Scan over the user's handful of tags + filter) — no content index needed, and no
        // sequential scan. This guards #19's acceptance for tags without an extra write-cost index.
        var user = await SeedOnceAsync();
        var planText = await ExplainAsync(
            $"""
             SELECT 1 FROM "Tags" t
             WHERE t."UserId" = '{user}' AND lower((t."Name")::text) LIKE '%zzqx%' ESCAPE '\'
             """);

        planText.Should().Contain("IX_Tags_UserId",
            $"the tag search must ride the UserId index; plan was:\n{planText}");
        planText.Should().NotContain("Seq Scan on \"Tags\"",
            $"the tag search must not fall back to a sequential scan; plan was:\n{planText}");
    }

    private async Task<string> ExplainAsync(string sql)
    {
        await using var ctx = fixture.CreateContext();
        var connection = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXPLAIN " + sql;
            var lines = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
            return string.Join("\n", lines);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task<Guid> SeedOnceAsync()
    {
        await SeedLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_seeded)
            {
                return _targetUser;
            }

            var targetUser = Guid.NewGuid();
            await using var ctx = fixture.CreateContext();

            // Many users so UserId is selective like production; the target user owns a slice.
            ctx.Users.Add(TestEntityFactory.User(targetUser).Build());
            for (var i = 0; i < 200; i++)
            {
                ctx.Users.Add(TestEntityFactory.User(Guid.NewGuid()).Build());
            }
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var connection = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            try
            {
                // ~50k products/URLs/tags via generate_series — far faster than per-row EF inserts.
                await Exec(connection,
                    """
                    INSERT INTO "Products" ("Id","Name","Currency","Status","IsFavourite","HasPriceAnomaly","UserId","CreatedAt","UpdatedAt")
                    SELECT gen_random_uuid(), 'Gadget '||g||' widget', 'USD', 0, false, false, u."Id", now(), now()
                    FROM "Users" u, generate_series(1, 250) g;
                    """);
                await Exec(connection,
                    """
                    INSERT INTO "ProductUrls" ("Id","Url","Currency","FailureCount","Status","SuspiciousCount","IsOutOfStock","SelectorType","ProductId","CreatedAt","UpdatedAt")
                    SELECT gen_random_uuid(),
                           'https://store.example.com/item/'||row_number() over ()||'/gadget-widget',
                           'USD', 0, 0, 0, false, 0, p."Id", now(), now()
                    FROM "Products" p;
                    """);
                await Exec(connection,
                    """
                    INSERT INTO "Tags" ("Id","Name","Color","Weight","UserId","CreatedAt","UpdatedAt")
                    SELECT gen_random_uuid(), 'Category '||g||' general', '#3B82F6', 0, u."Id", now(), now()
                    FROM "Users" u, generate_series(1, 250) g;
                    """);
                // VACUUM ANALYZE, one table at a time (VACUUM can't share an implicit transaction).
                // ANALYZE gives fresh selectivity stats; VACUUM flushes each GIN index's fastupdate
                // pending list. Without the flush, a fresh bulk load leaves new entries unmerged and the
                // planner estimates the GIN scan as costly and seq-scans instead — a transient bulk-load
                // artifact, not how the index behaves in production where autovacuum keeps it merged.
                await Exec(connection, "VACUUM ANALYZE \"Products\";");
                await Exec(connection, "VACUUM ANALYZE \"ProductUrls\";");
                await Exec(connection, "VACUUM ANALYZE \"Tags\";");
            }
            finally
            {
                await connection.CloseAsync();
            }

            _targetUser = targetUser;
            _seeded = true;
            return _targetUser;
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
