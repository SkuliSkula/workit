namespace Workit.Shared.Models;

/// <summary>
/// One price an item was bought at: the vendor's Ein.verð (without VAT, before our
/// discount) on one purchase line. Filled from every stored purchase line that starts
/// with the vendor's item number, on every expense sync — the price history by
/// product and by seller. One row per expense line.
/// </summary>
public sealed class PurchasePrice
{
    public Guid Id            { get; set; } = Guid.NewGuid();
    public Guid CompanyId     { get; set; }
    public Guid ExpenseLineId { get; set; }
    public Guid ExpenseId     { get; set; }

    public string Sku    { get; set; } = string.Empty;
    public string Name   { get; set; } = string.Empty;
    /// <summary>The seller, as Payday names the expense's creditor.</summary>
    public string Vendor { get; set; } = string.Empty;
    public DateTime? PurchasedOn { get; set; }

    /// <summary>The vendor's unit price without VAT, before our discount.</summary>
    public decimal EinVerdExVat       { get; set; }
    public decimal DiscountPercentage { get; set; }
    /// <summary>What we paid per unit without VAT, after the discount.</summary>
    public decimal PaidExVat          { get; set; }
    public decimal VatPercentage      { get; set; }
    public decimal Quantity           { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Who or what changed a price in the Payday price list.</summary>
public enum PriceChangeSource
{
    /// <summary>Workit, from a purchase's Ein.verð (price list from purchases).</summary>
    Purchase = 0,
    /// <summary>Someone in Payday (or the Products page) — seen on the product sync.</summary>
    Payday = 1,
}

/// <summary>
/// One change to a product's price in the Payday price list, kept as history.
/// <see cref="OldPriceExVat"/> is null when the change is the product being created.
/// </summary>
public sealed class PriceListChange
{
    public Guid Id              { get; set; } = Guid.NewGuid();
    public Guid CompanyId       { get; set; }
    public Guid PaydayProductId { get; set; }

    public string Sku  { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public decimal? OldPriceExVat { get; set; }
    public decimal  NewPriceExVat { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public PriceChangeSource Source { get; set; }
    /// <summary>The purchase behind a <see cref="PriceChangeSource.Purchase"/> change.</summary>
    public string?   Vendor      { get; set; }
    public DateTime? PurchasedOn { get; set; }
    /// <summary>The owner who applied it, or "Automatic" for the background run.</summary>
    public string?   By          { get; set; }
}

// ── What the Price tracker page reads ────────────────────────────────────────

/// <summary>One item in the tracker's list: its latest purchase price and how it moved since the one before.</summary>
public sealed record TrackedItem(
    string Sku, string Name,
    decimal LatestEinVerd, string LatestVendor, DateTime? LatestOn,
    decimal? PreviousEinVerd, DateTime? PreviousOn,
    int Purchases, int Vendors,
    decimal? PriceListPriceExVat)
{
    public decimal? ChangePercent => PreviousEinVerd is decimal p && p > 0 ? Math.Round((LatestEinVerd - p) / p * 100m, 1) : null;
}

/// <summary>Everything about one item: every purchase price, and every change to its price-list price.</summary>
public sealed record TrackedItemHistory(
    string Sku, string Name, decimal? PriceListPriceExVat,
    List<PurchasePrice> Purchases,
    List<PriceListChange> Changes);

/// <summary>One seller: how much was bought and how its prices moved.</summary>
public sealed record TrackedVendor(string Vendor, int Items, int Purchases, DateTime? LastOn, int ItemsUp, int ItemsDown);

/// <summary>Headline numbers for the top of the page.</summary>
public sealed record PriceTrackerSummary(int Items, int Vendors, int PriceListChanges30Days, int ItemsUp90Days, decimal? MedianIncreasePercent90Days);
