using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workit.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepriceAutoLinkedExpenseLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The same correction as RepriceExpenseLinesFromNetCost, for the lines
            // that arrived after it: expenses auto-linked by job code (and imported
            // from the console) were priced by PaydayExpenseMapper, which still
            // marked up the vendor's list price. Re-price from what was paid. Only
            // lines still on the automatic list-price figure are touched; billing
            // history keeps what it was invoiced at.
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
            // Not reversed: putting list-price figures back would reintroduce the bug,
            // and the earlier migration's Down already covers rolling the whole fix back.
        }
    }
}
