using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_content_type",
                table: "inventory_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_path",
                table: "inventory_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_content_type",
                table: "inventory_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "thumbnail_data",
                table: "inventory_items",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_content_type",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "image_path",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "thumbnail_content_type",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "thumbnail_data",
                table: "inventory_items");
        }
    }
}
