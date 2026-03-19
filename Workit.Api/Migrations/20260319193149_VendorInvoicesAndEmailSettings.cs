using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class VendorInvoicesAndEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarkupFactor",
                table: "Materials",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                table: "Materials",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "EmailSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImapHost = table.Column<string>(type: "text", nullable: false),
                    ImapPort = table.Column<int>(type: "integer", nullable: false),
                    UseSsl = table.Column<bool>(type: "boolean", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    InvoiceFolder = table.Column<string>(type: "text", nullable: false),
                    AutoScanEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VendorInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    VendorName = table.Column<string>(type: "text", nullable: false),
                    VendorEmail = table.Column<string>(type: "text", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SubtotalExVat = table.Column<decimal>(type: "numeric", nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalInclVat = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceEmailSubject = table.Column<string>(type: "text", nullable: false),
                    SourceEmailMessageId = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VendorInvoiceLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    ListPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric", nullable: false),
                    IsImported = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedMaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorInvoiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorInvoiceLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorInvoiceLineItems_VendorInvoices_VendorInvoiceId",
                        column: x => x.VendorInvoiceId,
                        principalTable: "VendorInvoices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSettings_CompanyId",
                table: "EmailSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorInvoiceLineItems_CompanyId_InvoiceId",
                table: "VendorInvoiceLineItems",
                columns: new[] { "CompanyId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorInvoiceLineItems_CompanyId_ProductCode",
                table: "VendorInvoiceLineItems",
                columns: new[] { "CompanyId", "ProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorInvoiceLineItems_VendorInvoiceId",
                table: "VendorInvoiceLineItems",
                column: "VendorInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorInvoices_CompanyId_InvoiceDate",
                table: "VendorInvoices",
                columns: new[] { "CompanyId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorInvoices_CompanyId_SourceEmailMessageId",
                table: "VendorInvoices",
                columns: new[] { "CompanyId", "SourceEmailMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailSettings");

            migrationBuilder.DropTable(
                name: "VendorInvoiceLineItems");

            migrationBuilder.DropTable(
                name: "VendorInvoices");

            migrationBuilder.DropColumn(
                name: "MarkupFactor",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                table: "Materials");
        }
    }
}
