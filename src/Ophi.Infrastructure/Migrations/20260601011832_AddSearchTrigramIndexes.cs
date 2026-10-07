using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ophi.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a pg_trgm GIN index on <c>ProductUrls.Url</c> so the product "contains" search is served by
    /// an index instead of a full sequential scan. A plain B-tree on <c>lower(col)</c> cannot serve a
    /// leading-wildcard LIKE — only a trigram GIN index can, hence pg_trgm.
    ///
    /// Scope and shape, both verified against real EXPLAIN plans (see Ophi.Postgres.Tests):
    ///
    /// 1. <b>Only ProductUrls.Url needs an index.</b> Every search is user-scoped (<c>WHERE UserId = …</c>),
    ///    so the Products.Name and Tags.Name predicates already ride the existing UserId composite
    ///    indexes (<c>IX_Products_UserId_Status</c>, <c>IX_Tags_UserId_Name</c>) and filter the handful of
    ///    rows a user owns — no seq scan. The exception is the product search's <c>OR EXISTS(… ProductUrls …)</c>
    ///    subplan: it is <em>not</em> user-scoped, so without this index it is a full Seq Scan of every
    ///    URL in the table. That is the one real hotspot, and the only place a content index is used.
    ///
    /// 2. The index expression must be <c>lower((col)::text)</c> — EF Core's <c>lower(col)</c> over a
    ///    <c>varchar</c> normalizes to <c>lower(("Url")::text)</c>; the index expression has to match that
    ///    canonical form or the planner silently ignores it and seq-scans anyway.
    ///
    /// Postgres-only and intentionally kept out of the EF model: the SQLite test tier builds its schema
    /// via EnsureCreated() against the model and has no pg_trgm, so this index lives in raw migration SQL
    /// that only the Npgsql migration path runs.
    /// </summary>
    /// <inheritdoc />
    public partial class AddSearchTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Not CONCURRENTLY — a migration runs inside a transaction. The expression matches EF's
            // emitted lower(("Url")::text) so the planner actually uses it.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ProductUrls_Url_trgm\" " +
                "ON \"ProductUrls\" USING gin (lower((\"Url\")::text) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ProductUrls_Url_trgm\";");
            // Leave the pg_trgm extension in place — other objects may depend on it.
        }
    }
}
