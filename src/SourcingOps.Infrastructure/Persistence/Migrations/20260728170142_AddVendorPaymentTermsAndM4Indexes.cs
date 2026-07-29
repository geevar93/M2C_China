using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPaymentTermsAndM4Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_documents_catalog_section_id",
                table: "catalog_documents");

            migrationBuilder.DropIndex(
                name: "ix_catalog_documents_is_latest",
                table: "catalog_documents");

            migrationBuilder.AddColumn<string>(
                name: "payment_terms",
                table: "vendors",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_region",
                table: "vendors",
                column: "region");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_sections_tags",
                table: "catalog_sections",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_documents_catalog_section_id_is_latest",
                table: "catalog_documents",
                columns: new[] { "catalog_section_id", "is_latest" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendors_region",
                table: "vendors");

            migrationBuilder.DropIndex(
                name: "ix_catalog_sections_tags",
                table: "catalog_sections");

            migrationBuilder.DropIndex(
                name: "ix_catalog_documents_catalog_section_id_is_latest",
                table: "catalog_documents");

            migrationBuilder.DropColumn(
                name: "payment_terms",
                table: "vendors");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_documents_catalog_section_id",
                table: "catalog_documents",
                column: "catalog_section_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_documents_is_latest",
                table: "catalog_documents",
                column: "is_latest");
        }
    }
}
