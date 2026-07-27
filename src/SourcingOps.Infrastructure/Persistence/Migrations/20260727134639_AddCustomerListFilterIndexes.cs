using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerListFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_customers_region",
                table: "customers",
                column: "region");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tags",
                table: "customers",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_region",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_tags",
                table: "customers");
        }
    }
}
