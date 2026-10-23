using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class PriceTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceListChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaydayProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OldPriceExVat = table.Column<decimal>(type: "numeric", nullable: true),
                    NewPriceExVat = table.Column<decimal>(type: "numeric", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Vendor = table.Column<string>(type: "text", nullable: true),
                    PurchasedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    By = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchasePrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Vendor = table.Column<string>(type: "text", nullable: false),
                    PurchasedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EinVerdExVat = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidExVat = table.Column<decimal>(type: "numeric", nullable: false),
                    VatPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasePrices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListChanges_CompanyId_ChangedAt",
                table: "PriceListChanges",
                columns: new[] { "CompanyId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListChanges_CompanyId_PaydayProductId",
                table: "PriceListChanges",
                columns: new[] { "CompanyId", "PaydayProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListChanges_CompanyId_Sku",
                table: "PriceListChanges",
                columns: new[] { "CompanyId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePrices_CompanyId_Sku",
                table: "PurchasePrices",
                columns: new[] { "CompanyId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePrices_CompanyId_Vendor",
                table: "PurchasePrices",
                columns: new[] { "CompanyId", "Vendor" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePrices_ExpenseLineId",
                table: "PurchasePrices",
                column: "ExpenseLineId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceListChanges");

            migrationBuilder.DropTable(
                name: "PurchasePrices");
        }
    }
}
