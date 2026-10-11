using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaydayInvoiceIdToBilledRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaydayInvoiceId",
                table: "TimeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaydayInvoiceId",
                table: "MaterialUsages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaydayInvoiceId",
                table: "ExpenseLineBillings",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaydayInvoiceId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "PaydayInvoiceId",
                table: "MaterialUsages");

            migrationBuilder.DropColumn(
                name: "PaydayInvoiceId",
                table: "ExpenseLineBillings");
        }
    }
}
