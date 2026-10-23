using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class PurchasesBillAtEinVerd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SalePriceSetByOwner",
                table: "ExpenseLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A line whose price is not what the old rule (paid × markup) worked out
            // was set by someone; repricing leaves it. (None on production when this
            // was written — every line matched — so this only guards other data.)
            migrationBuilder.Sql("""
                UPDATE "ExpenseLines"
                SET "SalePriceSetByOwner" = TRUE
                WHERE ROUND(COALESCE("UnitPriceExcludingVat", 0)
                            * (1 - LEAST(GREATEST(COALESCE("DiscountPercentage", 0), 0), 100) / 100)
                            * "MarkupFactor", 0) <> "SalePriceExcludingVat";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalePriceSetByOwner",
                table: "ExpenseLines");
        }
    }
}
