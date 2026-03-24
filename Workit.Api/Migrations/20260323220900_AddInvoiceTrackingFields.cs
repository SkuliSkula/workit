using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvoicedAt",
                table: "TimeEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiced",
                table: "TimeEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaydayInvoiceNumber",
                table: "TimeEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvoicedAt",
                table: "MaterialUsages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiced",
                table: "MaterialUsages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaydayInvoiceNumber",
                table: "MaterialUsages",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoicedAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "IsInvoiced",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "PaydayInvoiceNumber",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "InvoicedAt",
                table: "MaterialUsages");

            migrationBuilder.DropColumn(
                name: "IsInvoiced",
                table: "MaterialUsages");

            migrationBuilder.DropColumn(
                name: "PaydayInvoiceNumber",
                table: "MaterialUsages");
        }
    }
}
