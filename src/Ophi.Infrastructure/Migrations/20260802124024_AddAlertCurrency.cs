using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ophi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertCurrency : Migration
    {
        /// <summary>
        /// The backfill statement, exposed so the Postgres tier can execute the exact SQL that
        /// ships rather than a re-typed copy of it. A fresh database has no rows to backfill, so
        /// applying the migration proves only that it parses — see AlertCurrencyBackfillTests.
        /// </summary>
        public const string BackfillSql =
            """
            UPDATE "Alerts"
            SET "Currency" = COALESCE(
                (SELECT p."Currency" FROM "Products" p WHERE p."Id" = "Alerts"."ProductId"),
                'USD');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Alerts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            // Backfill before the column is trusted. The scaffolded "" default would leave every
            // pre-existing alert denominated in nothing, which matches no product currency — so the
            // new guard would read the entire existing alert set as mismatched and silently mute it.
            // Adopting each alert's product currency preserves today's behaviour: those alerts were
            // already being compared against that product's prices.
            //
            // Written as a correlated subquery rather than UPDATE..FROM so the one statement runs on
            // both Postgres and SQLite. COALESCE covers an alert whose product row has gone missing.
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Alerts");
        }
    }
}
