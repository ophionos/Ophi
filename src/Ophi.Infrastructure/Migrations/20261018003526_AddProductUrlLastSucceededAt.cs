using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ophi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUrlLastSucceededAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSucceededAt",
                table: "ProductUrls",
                type: "timestamp with time zone",
                nullable: true);

            // A row whose last check left no error and a price was last checked by a success (or an
            // out-of-stock read, which keeps the old price): the closest value the old columns hold.
            migrationBuilder.Sql(
                """
                UPDATE "ProductUrls" SET "LastSucceededAt" = "LastCheckedAt"
                WHERE "LastError" IS NULL AND "CurrentPrice" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSucceededAt",
                table: "ProductUrls");
        }
    }
}
