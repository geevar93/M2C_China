using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddM6InvoicingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paid_reference",
                table: "invoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "catalog_document_id",
                table: "dispatches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_id",
                table: "dispatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_status_history_invoice_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "invoice_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_status_history_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_status_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_invoice_id",
                table: "dispatches",
                column: "invoice_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dispatches_exactly_one_target",
                table: "dispatches",
                sql: "(catalog_document_id IS NOT NULL AND invoice_id IS NULL) OR (catalog_document_id IS NULL AND invoice_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_status_history_changed_by_user_id",
                table: "invoice_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_status_history_invoice_id_changed_at",
                table: "invoice_status_history",
                columns: new[] { "invoice_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_status_history_status_id",
                table: "invoice_status_history",
                column: "status_id");

            migrationBuilder.AddForeignKey(
                name: "fk_dispatches_invoices_invoice_id",
                table: "dispatches",
                column: "invoice_id",
                principalTable: "invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An invoice-targeted dispatch is UNREPRESENTABLE in the pre-M6 schema: that schema
            // requires a non-null catalog_document_id and these rows have none. Without this
            // delete, the AlterColumn at the end of this method would back-fill them with the
            // Guid.Empty default, pointing them at a catalog document that does not exist and
            // failing fk_dispatches_catalog_documents_catalog_document_id -- i.e. the down
            // migration would break outright the moment a single invoice dispatch existed.
            //
            // Deleting them is the honest down-path: the rows genuinely cannot be expressed in
            // the old shape, so this is real data loss being made explicit rather than an error
            // discovered at apply time. Reversibility has been a stated concern for this project
            // since N-17, and E2-07's deploy job runs migrations as an explicit step, so a Down()
            // that only works on an empty table is not good enough.
            //
            // After this delete, ck_dispatches_exactly_one_target guarantees every REMAINING row
            // has a non-null catalog_document_id, so the AlterColumn below is safe and its
            // Guid.Empty default can never actually be applied to a row.
            migrationBuilder.Sql("DELETE FROM dispatches WHERE invoice_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_dispatches_invoices_invoice_id",
                table: "dispatches");

            migrationBuilder.DropTable(
                name: "invoice_status_history");

            migrationBuilder.DropIndex(
                name: "ix_dispatches_invoice_id",
                table: "dispatches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dispatches_exactly_one_target",
                table: "dispatches");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "paid_reference",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                table: "dispatches");

            migrationBuilder.AlterColumn<Guid>(
                name: "catalog_document_id",
                table: "dispatches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
