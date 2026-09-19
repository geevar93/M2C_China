using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discount_type",
                table: "invoice_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_value",
                table: "invoice_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discount_type",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "discount_value",
                table: "invoice_lines");
        }
    }
}
