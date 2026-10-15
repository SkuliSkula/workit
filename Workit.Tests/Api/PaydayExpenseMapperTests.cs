using FluentAssertions;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Every Payday expense that becomes a Workit expense goes through this mapper —
/// auto-linked by job code, or imported from the console — so its sale price has
/// to start from what was actually paid, the same rule as ExpenseLine.DefaultSalePrice.
/// </summary>
public class PaydayExpenseMapperTests
{
    private static PaydayExpense Expense(params PaydayExpenseLine[] lines) => new()
    {
        Id = Guid.NewGuid(),
        AmountExcludingVat = 1432m,
        Lines = lines.ToList(),
    };

    private static PaydayExpenseLine Line(decimal? listPrice, decimal? discount, decimal quantity = 2m) => new()
    {
        Id = Guid.NewGuid(),
        Description = "9061-00 - Rofadós eftirá 68 mm D: 35mm (25stk)",
        Quantity = quantity,
        UnitPriceExcludingVat = listPrice,
        DiscountPercentage = discount,
        VatPercentage = 24m,
    };

    [Fact]
    public void A_discounted_line_is_priced_from_what_was_paid()
    {
        // The reported Rönning line: list 981, 27 % off, paid 716.13. Marked up 1.5
        // that is 1,074 — not the 1,472 that marking up the list price gives.
        var line = Expense(Line(981m, 27m)).ToWorkitExpense(jobId: null).Lines.Single();

        line.SalePriceExcludingVat.Should().Be(1074m);
        line.UnitPriceExcludingVat.Should().Be(981m, "the list price is kept as Payday reported it");
        line.DiscountPercentage.Should().Be(27m, "the vendor's discount is kept, but stays out of the price");
    }

    [Fact]
    public void The_mapper_and_the_import_endpoint_agree_on_the_price()
    {
        var mapped = Expense(Line(981m, 27m)).ToWorkitExpense(jobId: null).Lines.Single();

        mapped.SalePriceExcludingVat.Should().Be(
            ExpenseLine.DefaultSalePrice(981m, 27m, PaydayExpenseMapper.DefaultMarkupFactor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_line_without_a_discount_is_its_list_price_marked_up(int? discount)
    {
        var line = Expense(Line(981m, discount)).ToWorkitExpense(jobId: null).Lines.Single();

        line.SalePriceExcludingVat.Should().Be(1472m);
    }

    [Fact]
    public void A_line_with_no_price_maps_to_zero_rather_than_failing()
    {
        Expense(Line(null, 27m)).ToWorkitExpense(jobId: null).Lines.Single()
            .SalePriceExcludingVat.Should().Be(0m);
    }

    [Fact]
    public void Every_line_of_a_mixed_expense_is_priced_on_its_own_discount()
    {
        var lines = Expense(Line(981m, 27m), Line(2000m, 45m), Line(500m, null))
            .ToWorkitExpense(jobId: null).Lines;

        lines.Select(l => l.SalePriceExcludingVat).Should().Equal(1074m, 1650m, 750m);
    }
}
