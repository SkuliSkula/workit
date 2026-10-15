using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class OneCustomerPerPaydayCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_PaydayId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_PaydayId",
                table: "Customers",
                columns: new[] { "CompanyId", "PaydayId" },
                unique: true,
                filter: "\"PaydayId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_PaydayId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_PaydayId",
                table: "Customers",
                columns: new[] { "CompanyId", "PaydayId" },
                filter: "\"PaydayId\" IS NOT NULL");
        }
    }
}
