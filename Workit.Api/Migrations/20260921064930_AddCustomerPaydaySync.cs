using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPaydaySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaydayPushError",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaydayPushPending",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaydaySyncedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaydayPushError",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaydayPushPending",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaydaySyncedAt",
                table: "Customers");
        }
    }
}
