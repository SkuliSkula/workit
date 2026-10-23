using FluentAssertions;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Every Payday expense that becomes a Workit expense goes through this mapper —
/// auto-linked by job code, or imported from the console — so its sale price has
/// to start at the line's Ein.verð (the API then checks the reading against the expense
/// total through ExpensePricing).
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
    public void A_discounted_line_is_priced_at_its_Ein_verd()
    {
        // The Rönning line: Ein.verð 981, 27 % off (paid 716.13). The customer pays the
        // Ein.verð — no markup on what we paid.
        var line = Expense(Line(981m, 27m)).ToWorkitExpense(jobId: null).Lines.Single();

        line.SalePriceExcludingVat.Should().Be(981m);
        line.UnitPriceExcludingVat.Should().Be(981m, "the list price is kept as Payday reported it");
        line.DiscountPercentage.Should().Be(27m, "the vendor's discount is kept, but stays out of the price");
    }

    [Fact]
    public void The_mapper_and_the_import_endpoint_agree_on_the_price()
    {
        var mapped = Expense(Line(981m, 27m)).ToWorkitExpense(jobId: null).Lines.Single();

        mapped.SalePriceExcludingVat.Should().Be(
            ExpenseUnitPrice.SalePrice(mapped, UnitPriceReading.AsPaydayReports));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_line_without_a_discount_is_its_Ein_verd(int? discount)
    {
        var line = Expense(Line(981m, discount)).ToWorkitExpense(jobId: null).Lines.Single();

        line.SalePriceExcludingVat.Should().Be(981m);
    }

    [Fact]
    public void A_line_with_no_price_maps_to_zero_rather_than_failing()
    {
        Expense(Line(null, 27m)).ToWorkitExpense(jobId: null).Lines.Single()
            .SalePriceExcludingVat.Should().Be(0m);
    }

    [Fact]
    public void Every_line_of_a_mixed_expense_is_its_own_Ein_verd()
    {
        var lines = Expense(Line(981m, 27m), Line(2000m, 45m), Line(500m, null))
            .ToWorkitExpense(jobId: null).Lines;

        lines.Select(l => l.SalePriceExcludingVat).Should().Equal(981m, 2000m, 500m);
    }
}
