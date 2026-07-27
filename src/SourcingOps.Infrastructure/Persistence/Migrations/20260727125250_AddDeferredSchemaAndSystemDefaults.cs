using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeferredSchemaAndSystemDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_purchase_reference",
                table: "customers");

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "vendor_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reference",
                table: "shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "shipment_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "service_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "lead_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "invoice_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "external_marketplace",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_order_currency",
                table: "customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "external_order_date",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_order_ref",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "external_order_value",
                table: "customers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_supplier_name",
                table: "customers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "company_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    gstin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    registered_address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_ifsc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    invoice_number_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    logo_file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    declaration_text = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_settings", x => x.id);
                    table.CheckConstraint("ck_company_settings_singleton", "id = '00000000-0000-0000-0000-000000000001'");
                    table.ForeignKey(
                        name: "fk_company_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_reference",
                table: "shipments",
                column: "reference",
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_company_settings_updated_by_user_id",
                table: "company_settings",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_settings");

            migrationBuilder.DropIndex(
                name: "ix_shipments_reference",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "vendor_statuses");

            migrationBuilder.DropColumn(
                name: "reference",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "shipment_statuses");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "service_types");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "lead_statuses");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "invoice_statuses");

            migrationBuilder.DropColumn(
                name: "external_marketplace",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "external_order_currency",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "external_order_date",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "external_order_ref",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "external_order_value",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "external_supplier_name",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "categories");

            migrationBuilder.AddColumn<string>(
                name: "external_purchase_reference",
                table: "customers",
                type: "text",
                nullable: true);
        }
    }
}
