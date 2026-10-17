using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ophi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUrlLastErrorCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastErrorCategory",
                table: "ProductUrls",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastErrorCategory",
                table: "ProductUrls");
        }
    }
}
