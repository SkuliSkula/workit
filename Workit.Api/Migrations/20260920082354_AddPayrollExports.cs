using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayrollDrivingItemName",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing companies get the usual Icelandic payroll item names, same as the model default.
            migrationBuilder.AddColumn<string>(
                name: "PayrollOvertimeItemName",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "Yfirvinna");

            migrationBuilder.AddColumn<string>(
                name: "PayrollRegularItemName",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "Dagvinna");

            migrationBuilder.CreateTable(
                name: "PayrollExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentByName = table.Column<string>(type: "text", nullable: false),
                    EmployeesSent = table.Column<int>(type: "integer", nullable: false),
                    EmployeesRead = table.Column<int>(type: "integer", nullable: false),
                    SsnsNotOnRecord = table.Column<string>(type: "text", nullable: false),
                    PaydayPayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollExports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollExports_CompanyId_Year_Month",
                table: "PayrollExports",
                columns: new[] { "CompanyId", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollExports");

            migrationBuilder.DropColumn(
                name: "PayrollDrivingItemName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PayrollOvertimeItemName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PayrollRegularItemName",
                table: "Companies");
        }
    }
}
