using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepriceExpenseLinesFromNetCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expense sale prices were the vendor's LIST price marked up, with the
            // vendor's discount then passed on to the customer's invoice line. Re-
            // price from what we actually paid: list × (1 − discount) × markup.
            //
            // Only lines still on the automatic price are touched — a sale price an
            // owner typed in by hand is theirs. Billing history keeps the price it
            // was invoiced at; this only affects quantity not yet billed.
            migrationBuilder.Sql(@"
                UPDATE ""ExpenseLines""
                SET ""SalePriceExcludingVat"" =
                    round(coalesce(""UnitPriceExcludingVat"", 0) * (1 - ""DiscountPercentage"" / 100) * ""MarkupFactor"")
                WHERE coalesce(""DiscountPercentage"", 0) > 0
                  AND ""SalePriceExcludingVat"" = round(coalesce(""UnitPriceExcludingVat"", 0) * ""MarkupFactor"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ExpenseLines""
                SET ""SalePriceExcludingVat"" = round(coalesce(""UnitPriceExcludingVat"", 0) * ""MarkupFactor"")
                WHERE coalesce(""DiscountPercentage"", 0) > 0
                  AND ""SalePriceExcludingVat"" =
                      round(coalesce(""UnitPriceExcludingVat"", 0) * (1 - ""DiscountPercentage"" / 100) * ""MarkupFactor"");");
        }
    }
}
