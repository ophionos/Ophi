using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ophi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialScrapeClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InitialScrapeClaimedAt",
                table: "ProductUrls",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialScrapeClaimedAt",
                table: "ProductUrls");
        }
    }
}
