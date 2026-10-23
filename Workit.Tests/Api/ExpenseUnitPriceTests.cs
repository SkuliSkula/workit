using FluentAssertions;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// A purchase bills at its Ein.verð — the vendor's unit price without VAT, before
/// our discount, no markup. Vendors print Ein.verð with VAT (Húsasmiðjan, Ískraft)
/// or without (Rönning, Reykjafell), and could print it after the discount; the
/// expense's own total decides which reading is right.
/// </summary>
public class ExpenseUnitPriceTests
{
    private static ExpenseLine Line(decimal unit, decimal discount, decimal quantity = 1m, decimal vat = 24m) => new()
    {
        Quantity = quantity, UnitPriceExcludingVat = unit, DiscountPercentage = discount, VatPercentage = vat,
    };

    /// <summary>The Húsasmiðjan invoice from the report (Srk9296466): 8 lines, total 90,337 kr. without VAT.</summary>
    private static List<ExpenseLine> HusasmidjanInvoice() =>
    [
        Line(70395.16m, 45m), Line(6780.65m, 45m, 10), Line(4044.35m, 44.999999m), Line(1351.61m, 45m, 10),
        Line(1080.65m, 45.000002m), Line(829.84m, 44.999999m), Line(829.84m, 44.999999m), Line(319.35m, 44.999997m, 18),
    ];

    [Fact]
    public void The_reported_LED_strip_bills_at_its_Ein_verd()
    {
        var lines = HusasmidjanInvoice();
        var reading = ExpenseUnitPrice.Read(90337m, lines);

        reading.Should().Be(UnitPriceReading.AsPaydayReports, "the lines add up to the total only before VAT and before the discount");
        ExpenseUnitPrice.SalePrice(lines[0], reading).Should().Be(70395m, "not 58,076 — no markup on what we paid, the Ein.verð");
    }

    [Fact]
    public void An_Ein_verd_that_still_holds_the_VAT_is_caught_by_the_total()
    {
        // 10 × 12,400 with VAT, 20 % off: paid 10,000 × 0.8 × 10 = 80,000 without VAT.
        // Stored as if it were without VAT, the lines would add up to 99,200.
        var lines = new List<ExpenseLine> { Line(12400m, 20m, 10) };
        var reading = ExpenseUnitPrice.Read(80000m, lines);

        reading.Should().Be(new UnitPriceReading(IncludesVat: true, IncludesDiscount: false));
        ExpenseUnitPrice.SalePrice(lines[0], reading).Should().Be(10000m);
    }

    [Fact]
    public void An_Ein_verd_with_the_discount_already_off_is_caught_by_the_total()
    {
        // Vendor prints 800 (already 20 % off a 1,000 Ein.verð); the total is 5 × 800.
        var lines = new List<ExpenseLine> { Line(800m, 20m, 5) };
        var reading = ExpenseUnitPrice.Read(4000m, lines);

        reading.Should().Be(new UnitPriceReading(IncludesVat: false, IncludesDiscount: true));
        ExpenseUnitPrice.SalePrice(lines[0], reading).Should().Be(1000m);
    }

    [Fact]
    public void Both_at_once_is_caught_too()
    {
        // 992 printed = 1,000 Ein.verð − 20 % = 800, plus 24 % VAT. Total 3 × 800 = 2,400.
        var lines = new List<ExpenseLine> { Line(992m, 20m, 3) };
        var reading = ExpenseUnitPrice.Read(2400m, lines);

        reading.Should().Be(new UnitPriceReading(IncludesVat: true, IncludesDiscount: true));
        ExpenseUnitPrice.SalePrice(lines[0], reading).Should().Be(1000m);
    }

    [Fact]
    public void Without_a_discount_the_customer_pays_what_the_vendor_charged()
    {
        var lines = new List<ExpenseLine> { Line(981m, 0m, 2) };
        ExpenseUnitPrice.SalePrice(lines[0], ExpenseUnitPrice.Read(1962m, lines)).Should().Be(981m);
    }

    [Fact]
    public void When_nothing_adds_up_Paydays_reading_is_used()
    {
        // A total that includes a freight charge with no line of its own.
        var lines = new List<ExpenseLine> { Line(1000m, 10m, 2) };
        ExpenseUnitPrice.Read(2500m, lines).Should().Be(UnitPriceReading.AsPaydayReports);
    }

    [Fact]
    public void With_no_VAT_and_no_discounts_the_readings_agree_and_Paydays_is_used()
    {
        var lines = new List<ExpenseLine> { Line(5000m, 0m, 1, vat: 0m) };
        ExpenseUnitPrice.Read(5000m, lines).Should().Be(UnitPriceReading.AsPaydayReports);
    }

    [Fact]
    public void A_return_keeps_its_minus_sign_so_it_cancels_the_purchase()
    {
        // Húsasmiðjan credit for an LED driver: −6,826 before the 45 % discount.
        var lines = new List<ExpenseLine> { Line(-6826m, 45m) };
        var reading = ExpenseUnitPrice.Read(-3754.30m, lines);
        ExpenseUnitPrice.SalePrice(lines[0], reading).Should().Be(-6826m);
    }

    [Fact]
    public void An_expense_with_no_lines_is_read_as_Payday_reports_it()
    {
        ExpenseUnitPrice.Read(1000m, []).Should().Be(UnitPriceReading.AsPaydayReports);
    }
}
