using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class StoreInvoicesFromPayday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId_PaydayId",
                table: "Invoices");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_PaydayId",
                table: "Invoices",
                columns: new[] { "CompanyId", "PaydayId" },
                unique: true,
                filter: "\"PaydayId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId_PaydayId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_PaydayId",
                table: "Invoices",
                columns: new[] { "CompanyId", "PaydayId" },
                filter: "\"PaydayId\" IS NOT NULL");
        }
    }
}
