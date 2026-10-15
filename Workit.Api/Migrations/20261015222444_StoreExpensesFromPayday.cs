using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class StoreExpensesFromPayday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_CompanyId_PaydayId",
                table: "Expenses");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncedAt",
                table: "Expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_PaydayId",
                table: "Expenses",
                columns: new[] { "CompanyId", "PaydayId" },
                unique: true,
                filter: "\"PaydayId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_CompanyId_PaydayId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_PaydayId",
                table: "Expenses",
                columns: new[] { "CompanyId", "PaydayId" },
                filter: "\"PaydayId\" IS NOT NULL");
        }
    }
}
