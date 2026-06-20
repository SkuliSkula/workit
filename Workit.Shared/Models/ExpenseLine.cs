namespace Workit.Shared.Models;

public sealed class ExpenseLine
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public Guid CompanyId { get; set; }

    public string  Description           { get; set; } = string.Empty;
    public decimal Quantity              { get; set; }

    /// <summary>Cost excl. VAT — what we paid the vendor (per unit).</summary>
    public decimal? UnitPriceExcludingVat { get; set; }
    /// <summary>Cost incl. VAT — what we paid the vendor (per unit).</summary>
    public decimal? UnitPriceIncludingVat { get; set; }

    /// <summary>Markup multiplier applied to the cost to derive the sale price. Default 1.5 = 50% markup.</summary>
    public decimal MarkupFactor { get; set; } = 1.5m;

    /// <summary>Sale price excl. VAT (per unit) — what we charge the customer. = cost × MarkupFactor, or set manually.</summary>
    public decimal SalePriceExcludingVat { get; set; }

    public decimal VatPercentage         { get; set; }
    public decimal? DiscountPercentage   { get; set; }

    /// <summary>Payday accounting account id.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Payday line id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }

    /// <summary>Partial billings of this line (e.g. bill 50m of 100m now, the rest later).</summary>
    public List<ExpenseLineBilling> Billings { get; set; } = [];
}
