using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicesAndExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditorPaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditorSsn = table.Column<string>(type: "text", nullable: true),
                    CreditorName = table.Column<string>(type: "text", nullable: true),
                    PaymentTypePaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTypeName = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Deductible = table.Column<bool>(type: "boolean", nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    Voucher = table.Column<string>(type: "text", nullable: true),
                    AmountExcludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    AmountIncludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    AmountVat = table.Column<decimal>(type: "numeric", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerSsn = table.Column<string>(type: "text", nullable: true),
                    CustomerName = table.Column<string>(type: "text", nullable: true),
                    PayorPaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayorSsn = table.Column<string>(type: "text", nullable: true),
                    PayorName = table.Column<string>(type: "text", nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CurrencyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreditDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimFinalDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimCancelledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    AmountExcludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    AmountIncludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    AmountVat = table.Column<decimal>(type: "numeric", nullable: false),
                    ForeignAmountExcludingVat = table.Column<decimal>(type: "numeric", nullable: true),
                    ForeignAmountIncludingVat = table.Column<decimal>(type: "numeric", nullable: true),
                    ForeignAmountVat = table.Column<decimal>(type: "numeric", nullable: true),
                    VatNumber = table.Column<string>(type: "text", nullable: true),
                    CreateClaim = table.Column<bool>(type: "boolean", nullable: false),
                    CreateElectronicInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    ElectronicInvoicePartyId = table.Column<string>(type: "text", nullable: true),
                    SendEmail = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultInterest = table.Column<decimal>(type: "numeric", nullable: false),
                    CapitalGainsTax = table.Column<decimal>(type: "numeric", nullable: false),
                    AccountingCost = table.Column<string>(type: "text", nullable: true),
                    Ocr = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPriceExcludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPriceIncludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    VatPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseLines_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPriceExcludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPriceIncludingVat = table.Column<decimal>(type: "numeric", nullable: false),
                    ForeignUnitPriceExcludingVat = table.Column<decimal>(type: "numeric", nullable: true),
                    ForeignUnitPriceIncludingVat = table.Column<decimal>(type: "numeric", nullable: true),
                    VatPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    Sku = table.Column<string>(type: "text", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTypePaydayId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTypeName = table.Column<string>(type: "text", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: true),
                    PaydayId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_CompanyId_ExpenseId",
                table: "ExpenseLines",
                columns: new[] { "CompanyId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_ExpenseId",
                table: "ExpenseLines",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_Date",
                table: "Expenses",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_JobId",
                table: "Expenses",
                columns: new[] { "CompanyId", "JobId" },
                filter: "\"JobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_PaydayId",
                table: "Expenses",
                columns: new[] { "CompanyId", "PaydayId" },
                filter: "\"PaydayId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_Status",
                table: "Expenses",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_CompanyId_InvoiceId",
                table: "InvoiceLines",
                columns: new[] { "CompanyId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_CompanyId_InvoiceId",
                table: "InvoicePayments",
                columns: new[] { "CompanyId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "CompanyId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_PaydayId",
                table: "Invoices",
                columns: new[] { "CompanyId", "PaydayId" },
                filter: "\"PaydayId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_Status",
                table: "Invoices",
                columns: new[] { "CompanyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseLines");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
