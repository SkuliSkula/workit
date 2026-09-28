using FluentAssertions;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Payday reports an expense line's list price and the vendor's discount
/// separately, and the expense total is net of the discount. The sale price
/// has to start from what was actually paid.
/// </summary>
public class ExpenseSalePriceTests
{
    [Fact]
    public void The_reported_Ronning_line_prices_from_what_was_paid()
    {
        // 9061-00 Rofadós: list 981, 27 % off, 1.5 markup. Paid 716.13.
        var line = new ExpenseLine { UnitPriceExcludingVat = 981m, DiscountPercentage = 27m, MarkupFactor = 1.5m };

        line.NetUnitCostExcludingVat.Should().Be(716.13m);
        ExpenseLine.DefaultSalePrice(981m, 27m, 1.5m).Should().Be(1074m);
    }

    [Fact]
    public void The_old_price_was_the_list_price_marked_up()
    {
        // What was stored before: 981 × 1.5 = 1,472, which only came out right
        // while the 27 % rode along on the customer's line.
        Math.Round(981m * 1.5m, 0, MidpointRounding.AwayFromZero).Should().Be(1472m);
        ExpenseLine.DefaultSalePrice(981m, 27m, 1.5m).Should().BeLessThan(1472m);
    }

    [Fact]
    public void Without_a_discount_nothing_changes()
    {
        ExpenseLine.DefaultSalePrice(981m, null, 1.5m).Should().Be(1472m);
        ExpenseLine.DefaultSalePrice(981m, 0m, 1.5m).Should().Be(1472m);
    }

    [Fact]
    public void A_nonsense_discount_is_clamped_rather_than_turning_the_price_negative()
    {
        ExpenseLine.DefaultSalePrice(1000m, 150m, 1.5m).Should().Be(0m);
        ExpenseLine.DefaultSalePrice(1000m, -10m, 1.5m).Should().Be(1500m);
    }

    [Fact]
    public void The_customer_pays_the_same_as_before_when_nobody_touched_the_discount()
    {
        // Old: 1,472 with 27 % on the invoice line. New: 1,074 with none.
        // Same money to the customer — the fix is that it stays right when the
        // invoice discount is changed.
        var oldPerUnit = 1472m * (1 - 0.27m);
        var newPerUnit = ExpenseLine.DefaultSalePrice(981m, 27m, 1.5m);
        Math.Abs(oldPerUnit - newPerUnit).Should().BeLessThan(1m);
    }

    [Fact]
    public void A_ten_percent_customer_discount_no_longer_wipes_out_the_vendor_discount()
    {
        // Before: setting the line to 10 % replaced the vendor's 27 %, so the
        // customer paid 1,472 × 0.9 = 1,324.80 a unit instead of about 967.
        var correct = ExpenseLine.DefaultSalePrice(981m, 27m, 1.5m) * 0.9m;
        var wasCharged = 1472m * 0.9m;
        correct.Should().BeApproximately(966.6m, 0.5m);
        (wasCharged / correct).Should().BeGreaterThan(1.36m, "the old path overcharged by over a third");
    }
}
