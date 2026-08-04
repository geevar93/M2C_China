using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryStockAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_stock_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_qty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    previous_qty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    adjusted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    adjusted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    adjusted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_stock_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_stock_adjustments_inventory_items_inventory_item_",
                        column: x => x.inventory_item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_stock_adjustments_users_adjusted_by_user_id",
                        column: x => x.adjusted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_stock_adjustments_adjusted_by_user_id",
                table: "inventory_stock_adjustments",
                column: "adjusted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_stock_adjustments_inventory_item_id_adjusted_on",
                table: "inventory_stock_adjustments",
                columns: new[] { "inventory_item_id", "adjusted_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_stock_adjustments");
        }
    }
}
