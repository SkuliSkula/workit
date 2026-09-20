using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaydayProductCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaydayProductId",
                table: "Materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaydayProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SalePriceExVat = table.Column<decimal>(type: "numeric", nullable: false),
                    SalePriceIncVat = table.Column<decimal>(type: "numeric", nullable: false),
                    VatPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    SalesLedgerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaydayProducts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaydayProducts_CompanyId_PaydayId",
                table: "PaydayProducts",
                columns: new[] { "CompanyId", "PaydayId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaydayProducts_CompanyId_Sku",
                table: "PaydayProducts",
                columns: new[] { "CompanyId", "Sku" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaydayProducts");

            migrationBuilder.DropColumn(
                name: "PaydayProductId",
                table: "Materials");
        }
    }
}
