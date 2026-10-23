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

    /// <summary>
    /// No longer prices anything: purchases bill at their Ein.verð with no markup
    /// (see ExpenseUnitPrice), and repricing sets this to 1. Kept as history.
    /// </summary>
    public decimal MarkupFactor { get; set; } = 1.5m;

    /// <summary>Sale price excl. VAT per unit — what the customer is billed: the Ein.verð, or an owner's own price.</summary>
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
    /// True once an owner set this line's sale price themselves; repricing then
    /// leaves it alone. Otherwise the price is the Ein.verð (see ExpenseUnitPrice).
    /// </summary>
    public bool SalePriceSetByOwner { get; set; }

    /// <summary>Payday accounting account id.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Payday line id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }

    /// <summary>Partial billings of this line (e.g. bill 50m of 100m now, the rest later).</summary>
    public List<ExpenseLineBilling> Billings { get; set; } = [];
}
