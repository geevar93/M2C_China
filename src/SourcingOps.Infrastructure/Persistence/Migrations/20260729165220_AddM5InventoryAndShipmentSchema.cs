using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The single reviewed M5 (E7) schema migration. Keeps the M1 freeze (DR-2) breaking once
    /// per pass with review, exactly as the pre-M3 migration and D-20's
    /// AddVendorPaymentTermsAndM4Indexes did. Carries, by deviation:
    ///
    ///   D-a  inventory_items.unit_cost           — the Stock Value column / On-Hand Value tile
    ///   D-b  shipment_lines.unit_cost            — snapshotted per line for E8's invoice basis
    ///   D-d  inventory_inbound_entries (table)   — E7-02's durable "date and reference" record
    ///   D-e  shipment_status_history (table)     — E7-07's timestamps / the detail stepper
    ///   D-f  document_types.scope                — Vendor|Shipment, existing rows backfilled
    ///   D-f  shipment_documents.document_type_id — replaces the free-text doc_type column
    ///        inventory_items.description         — named by E7-01, absent from TECH_SPEC §6
    ///        shipment_documents.size_bytes       — the screen's "184 KB · 21 Jul 2026" meta
    ///        shipment_documents.uploaded_by_user_id — parity with vendor_documents, Auditability NFR
    ///
    /// plus the E7-03/E7-08 filter indexes.
    ///
    /// REVIEW NOTE — the one operation here that is not trivially safe.
    /// `document_type_id` and `uploaded_by_user_id` are added to shipment_documents as NOT NULL
    /// with an all-zeros GUID default, and the free-text `doc_type` column is dropped. Both are
    /// safe ONLY because shipment_documents is provably empty in every environment: no code path
    /// has ever written to it. There was no ShipmentsController, no ShipmentDocumentService and
    /// no other reference to IAppDbContext.ShipmentDocuments before this pass — the table was
    /// created by the E1-02 InitialCreate and left unwritten. Verified live against a clean
    /// volume before this migration was accepted.
    ///
    /// If that assumption were ever wrong, this migration FAILS LOUDLY rather than corrupting:
    /// the AddForeignKey calls at the end of Up() would reject the zero-GUID rows with an FK
    /// violation and roll the whole migration back. It cannot silently leave a document row
    /// pointing at a non-existent document type or user. It does mean the dropped `doc_type`
    /// text would be unrecoverable in that scenario — which is why the emptiness precondition is
    /// stated here rather than assumed.
    ///
    /// Down() is a true structural inverse and has been hand-checked: it drops both new tables,
    /// every added column and index, and restores `doc_type`. It deliberately does NOT restore
    /// any dropped `doc_type` VALUES (there are none) and does not un-backfill document_types.scope
    /// (the column is dropped outright, so the derived value goes with it).
    /// </summary>
    public partial class AddM5InventoryAndShipmentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Superseded by the (status_id, dispatch_date) composite created below — a btree
            // composite serves any leading-column predicate, so keeping both would cost write
            // throughput for no read benefit. See ShipmentConfiguration's comment.
            migrationBuilder.DropIndex(
                name: "ix_shipments_status_id",
                table: "shipments");

            // D-f: replaced by document_type_id, added below. Safe only on an empty table —
            // see the class-level REVIEW NOTE.
            migrationBuilder.DropColumn(
                name: "doc_type",
                table: "shipment_documents");

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "shipment_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_type_id",
                table: "shipment_documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "shipment_documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "shipment_documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "inventory_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "inventory_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "document_types",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Vendor");

            migrationBuilder.CreateTable(
                name: "inventory_inbound_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_inbound_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_inbound_entries_inventory_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_inbound_entries_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_status_history_shipment_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "shipment_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_status_history_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipment_status_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status_id_dispatch_date",
                table: "shipments",
                columns: new[] { "status_id", "dispatch_date" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_documents_document_type_id",
                table: "shipment_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_documents_uploaded_at",
                table: "shipment_documents",
                column: "uploaded_at");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_documents_uploaded_by_user_id",
                table: "shipment_documents",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_on_hand_qty",
                table: "inventory_items",
                column: "on_hand_qty");

            migrationBuilder.CreateIndex(
                name: "ix_document_types_scope",
                table: "document_types",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_inbound_entries_inventory_item_id_entry_date",
                table: "inventory_inbound_entries",
                columns: new[] { "inventory_item_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_inbound_entries_recorded_by_user_id",
                table: "inventory_inbound_entries",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_status_history_changed_by_user_id",
                table: "shipment_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_status_history_shipment_id_changed_at",
                table: "shipment_status_history",
                columns: new[] { "shipment_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_status_history_status_id",
                table: "shipment_status_history",
                column: "status_id");

            migrationBuilder.AddForeignKey(
                name: "fk_shipment_documents_document_types_document_type_id",
                table: "shipment_documents",
                column: "document_type_id",
                principalTable: "document_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_shipment_documents_users_uploaded_by_user_id",
                table: "shipment_documents",
                column: "uploaded_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shipment_documents_document_types_document_type_id",
                table: "shipment_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_documents_users_uploaded_by_user_id",
                table: "shipment_documents");

            migrationBuilder.DropTable(
                name: "inventory_inbound_entries");

            migrationBuilder.DropTable(
                name: "shipment_status_history");

            migrationBuilder.DropIndex(
                name: "ix_shipments_status_id_dispatch_date",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipment_documents_document_type_id",
                table: "shipment_documents");

            migrationBuilder.DropIndex(
                name: "ix_shipment_documents_uploaded_at",
                table: "shipment_documents");

            migrationBuilder.DropIndex(
                name: "ix_shipment_documents_uploaded_by_user_id",
                table: "shipment_documents");

            migrationBuilder.DropIndex(
                name: "ix_inventory_items_on_hand_qty",
                table: "inventory_items");

            migrationBuilder.DropIndex(
                name: "ix_document_types_scope",
                table: "document_types");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "document_type_id",
                table: "shipment_documents");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "shipment_documents");

            migrationBuilder.DropColumn(
                name: "uploaded_by_user_id",
                table: "shipment_documents");

            migrationBuilder.DropColumn(
                name: "description",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "document_types");

            migrationBuilder.AddColumn<string>(
                name: "doc_type",
                table: "shipment_documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status_id",
                table: "shipments",
                column: "status_id");
        }
    }
}
