namespace Workit.Shared.Models;

/// <summary>
/// How an expense's unit prices (the vendor's Ein.verð) are to be read. Vendors
/// print them differently — Húsasmiðjan and Ískraft with VAT, Rönning and
/// Reykjafell without — and a vendor could send a unit price with its discount
/// already taken off. Payday normally splits each price into with and without
/// VAT; this checks that against the expense itself rather than trusting it.
/// </summary>
public sealed record UnitPriceReading(bool IncludesVat, bool IncludesDiscount)
{
    /// <summary>Payday's own reading: without VAT, before the vendor's discount.</summary>
    public static readonly UnitPriceReading AsPaydayReports = new(false, false);
}

/// <summary>
/// A purchase is billed to the customer at its Ein.verð — the vendor's unit price
/// without VAT, before the discount the vendor gave us — with no markup. Which
/// way the stored unit price has to be read is decided by the expense: the lines
/// must add up to the total the vendor actually charged.
/// </summary>
public static class ExpenseUnitPrice
{
    private static readonly UnitPriceReading[] Readings =
    [
        UnitPriceReading.AsPaydayReports,
        new(IncludesVat: true,  IncludesDiscount: false),
        new(IncludesVat: false, IncludesDiscount: true),
        new(IncludesVat: true,  IncludesDiscount: true),
    ];

    /// <summary>
    /// The one reading under which the expense's lines add up to its total
    /// without VAT (within 2 kr. or 0.5 %). Payday's reading when it fits — and when
    /// nothing fits, or the readings can't be told apart (no VAT, no discounts),
    /// since then they all give the same price or there is nothing better to go on.
    /// </summary>
    public static UnitPriceReading Read(decimal expenseTotalExcludingVat, IReadOnlyCollection<ExpenseLine> lines)
    {
        if (lines.Count == 0) return UnitPriceReading.AsPaydayReports;
        var tolerance = Math.Max(2m, Math.Abs(expenseTotalExcludingVat) * 0.005m);
        bool Fits(UnitPriceReading r) => Math.Abs(lines.Sum(l => l.Quantity * PaidExVat(l, r)) - expenseTotalExcludingVat) <= tolerance;

        if (Fits(UnitPriceReading.AsPaydayReports)) return UnitPriceReading.AsPaydayReports;
        var fitting = Readings.Skip(1).Where(Fits).ToList();
        return fitting.Count == 1 ? fitting[0] : UnitPriceReading.AsPaydayReports;
    }

    /// <summary>The Ein.verð without VAT, before the vendor's discount — what the customer is billed per unit.</summary>
    public static decimal EinVerdExVat(ExpenseLine line, UnitPriceReading reading)
    {
        var unit = line.UnitPriceExcludingVat ?? 0m;
        if (reading.IncludesVat) unit /= 1m + line.VatPercentage / 100m;
        if (reading.IncludesDiscount)
        {
            var kept = 1m - Discount(line);
            if (kept > 0) unit /= kept;
        }
        return unit;
    }

    /// <summary>The sale price per unit, rounded to whole krónur. A return keeps its minus sign.</summary>
    public static decimal SalePrice(ExpenseLine line, UnitPriceReading reading) =>
        Math.Round(EinVerdExVat(line, reading), 0, MidpointRounding.AwayFromZero);

    /// <summary>What was paid per unit without VAT, under a reading.</summary>
    private static decimal PaidExVat(ExpenseLine line, UnitPriceReading reading)
    {
        var unit = line.UnitPriceExcludingVat ?? 0m;
        if (reading.IncludesVat) unit /= 1m + line.VatPercentage / 100m;
        return reading.IncludesDiscount ? unit : unit * (1m - Discount(line));
    }

    private static decimal Discount(ExpenseLine line) => Math.Clamp(line.DiscountPercentage ?? 0m, 0m, 100m) / 100m;
}
