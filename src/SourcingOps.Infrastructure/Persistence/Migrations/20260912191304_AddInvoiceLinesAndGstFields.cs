using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLinesAndGstFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_intra_state",
                table: "invoices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "place_of_supply_state_code",
                table: "invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gst_rate",
                table: "inventory_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hsn_code",
                table: "inventory_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "selling_price",
                table: "inventory_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state_code",
                table: "customers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state_code",
                table: "company_settings",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    hsn_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gst_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_lines_inventory_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_inventory_item_id",
                table: "invoice_lines",
                column: "inventory_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id_sort_order",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "sort_order" });

            // Backfill place-of-supply state codes from the GSTINs already on file. A GSTIN's
            // first two digits ARE the state code, so this is a derivation, not a guess - and
            // it is the difference between an existing customer being invoiceable immediately
            // and someone having to re-key a value the row already contains.
            //
            // Guarded three ways: only rows that have a GSTIN, only where the prefix is two
            // digits (a malformed GSTIN is left alone rather than truncated into a wrong
            // state), and only where state_code is still null so a hand-set value is never
            // overwritten. Rows that do not qualify stay null and are caught later by the
            // issue-time check, which refuses rather than assuming a tax treatment.
            migrationBuilder.Sql(@"
                UPDATE customers
                SET state_code = substring(gstin from 1 for 2)
                WHERE state_code IS NULL
                  AND gstin IS NOT NULL
                  AND substring(gstin from 1 for 2) ~ '^[0-9]{2}$';");

            migrationBuilder.Sql(@"
                UPDATE company_settings
                SET state_code = substring(gstin from 1 for 2)
                WHERE state_code IS NULL
                  AND gstin IS NOT NULL
                  AND substring(gstin from 1 for 2) ~ '^[0-9]{2}$';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "is_intra_state",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "place_of_supply_state_code",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "gst_rate",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "hsn_code",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "selling_price",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "state_code",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "state_code",
                table: "company_settings");
        }
    }
}
