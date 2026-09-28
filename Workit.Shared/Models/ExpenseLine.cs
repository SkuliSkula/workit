namespace Workit.Shared.Models;

public sealed class ExpenseLine
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public Guid CompanyId { get; set; }

    public string  Description           { get; set; } = string.Empty;
    public decimal Quantity              { get; set; }

    /// <summary>
    /// The vendor's unit price excl. VAT as it appears on their invoice — <em>before</em>
    /// their discount. Payday reports the discount separately in
    /// <see cref="DiscountPercentage"/>, and the expense total is net of it: a
    /// Rönning line of 2 × 981 at 27 % off totals 1,432, not 1,962. What we
    /// actually paid per unit is <see cref="NetUnitCostExcludingVat"/>.
    /// </summary>
    public decimal? UnitPriceExcludingVat { get; set; }
    /// <summary>The same list price incl. VAT, before the vendor's discount.</summary>
    public decimal? UnitPriceIncludingVat { get; set; }

    /// <summary>Markup multiplier applied to the cost to derive the sale price. Default 1.5 = 50% markup.</summary>
    public decimal MarkupFactor { get; set; } = 1.5m;

    /// <summary>Sale price excl. VAT (per unit) — what we charge the customer. = cost × MarkupFactor, or set manually.</summary>
    public decimal SalePriceExcludingVat { get; set; }

    public decimal VatPercentage         { get; set; }
    /// <summary>
    /// The vendor's discount to us on this line — a purchase term, never the
    /// customer's. It is already inside the sale price, so it is not repeated
    /// on the invoice.
    /// </summary>
    public decimal? DiscountPercentage   { get; set; }

    /// <summary>What we actually paid per unit excl. VAT: the list price less the vendor's discount.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal NetUnitCostExcludingVat =>
        (UnitPriceExcludingVat ?? 0m) * (1m - Math.Clamp(DiscountPercentage ?? 0m, 0m, 100m) / 100m);

    /// <summary>
    /// The default sale price: what we paid, marked up. Built on the net cost —
    /// marking up the list price and then passing the vendor's discount on to the
    /// customer lands on roughly the same total, but only while nobody touches
    /// the discount; change it on the invoice and the customer is overcharged.
    /// </summary>
    public static decimal DefaultSalePrice(decimal? listUnitPrice, decimal? discountPercentage, decimal markupFactor) =>
        Math.Round(
            (listUnitPrice ?? 0m) * (1m - Math.Clamp(discountPercentage ?? 0m, 0m, 100m) / 100m) * markupFactor,
            0, MidpointRounding.AwayFromZero);

    /// <summary>Payday accounting account id.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Payday line id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }

    /// <summary>Partial billings of this line (e.g. bill 50m of 100m now, the rest later).</summary>
    public List<ExpenseLineBilling> Billings { get; set; } = [];
}
