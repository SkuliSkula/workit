using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseMarkupAndBillings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceIncludingVat",
                table: "ExpenseLines",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceExcludingVat",
                table: "ExpenseLines",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<decimal>(
                name: "MarkupFactor",
                table: "ExpenseLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePriceExcludingVat",
                table: "ExpenseLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ExpenseLineBillings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    SalePriceExcludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    VatPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    PaydayInvoiceNumber = table.Column<int>(type: "integer", nullable: true),
                    InvoiceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    BilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseLineBillings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseLineBillings_ExpenseLines_ExpenseLineId",
                        column: x => x.ExpenseLineId,
                        principalTable: "ExpenseLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLineBillings_CompanyId_ExpenseLineId",
                table: "ExpenseLineBillings",
                columns: new[] { "CompanyId", "ExpenseLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLineBillings_CompanyId_JobId",
                table: "ExpenseLineBillings",
                columns: new[] { "CompanyId", "JobId" },
                filter: "\"JobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLineBillings_ExpenseLineId",
                table: "ExpenseLineBillings",
                column: "ExpenseLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseLineBillings");

            migrationBuilder.DropColumn(
                name: "MarkupFactor",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "SalePriceExcludingVat",
                table: "ExpenseLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceIncludingVat",
                table: "ExpenseLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceExcludingVat",
                table: "ExpenseLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
