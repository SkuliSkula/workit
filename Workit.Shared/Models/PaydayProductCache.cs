namespace Workit.Shared.Models;

/// <summary>
/// What a Payday product is <em>for</em> in Workit. Payday has no product
/// type — a cable and "Vinna, klst." are the same object — so the owner says.
/// Defaults from whether Payday tracks stock for it (Material) or not
/// (Unassigned); the three labor roles must be assigned before an invoice can
/// be built by product.
/// </summary>
public enum PaydayProductRole
{
    Unassigned   = 0,
    Material     = 1,
    RegularHour  = 2,
    OvertimeHour = 3,
    DrivingUnit  = 4,
    Ignore       = 5,
}

/// <summary>
/// Workit's copy of one Payday product, written only by the sync. Payday owns
/// name, SKU, prices, VAT, ledger account and stock; Workit adds the role and
/// two display fields Payday lacks (unit, category). See docs/payday-spike.md.
/// </summary>
public sealed class PaydayProductCache
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }

    /// <summary>Payday's product id; unique per company.</summary>
    public Guid PaydayId { get; set; }

    // ── Mirrored from Payday ─────────────────────────────────────────────────
    public string  Sku                  { get; set; } = string.Empty;
    public string  Name                 { get; set; } = string.Empty;
    public string  Description          { get; set; } = string.Empty;
    public decimal SalePriceExVat       { get; set; }
    public decimal SalePriceIncVat      { get; set; }
    public decimal VatPercentage        { get; set; }
    public Guid?   SalesLedgerAccountId { get; set; }
    /// <summary>Stock on hand in Payday; null when Payday does not track stock for it.</summary>
    public decimal? Quantity            { get; set; }
    public bool    Archived             { get; set; }
    public string  Tags                 { get; set; } = string.Empty;
    public DateTimeOffset SyncedAt      { get; set; }

    // ── Workit's own ─────────────────────────────────────────────────────────
    public PaydayProductRole Role { get; set; } = PaydayProductRole.Unassigned;
    /// <summary>Unit of measure shown in the apps: m., stk., klst. Payday has none.</summary>
    public string Unit     { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>True for a product Payday tracks stock for — the default for the Material role.</summary>
    public bool IsStockTracked => Quantity is not null;
}

/// <summary>Outcome of one products sync, returned to the console.</summary>
public sealed record PaydayProductSyncResult(int Fetched, int Added, int Updated, int Archived, DateTimeOffset SyncedAt);

/// <summary>Body of PUT /api/payday/products/{id}/role.</summary>
public sealed record PaydayProductRoleUpdate(PaydayProductRole Role, string? Unit, string? Category);

/// <summary>
/// A product the owner creates from Workit. It is created in Payday (which owns
/// the price list) and lands in the cache with its Workit role in one go.
/// </summary>
public sealed record PaydayProductCreate(
    string Name,
    string Sku,
    string? Description,
    decimal SalePriceExVat,
    decimal VatPercentage,
    Guid SalesLedgerAccountId,
    /// <summary>Stock-tracked in Payday (a material) or a service line (labor, driving).</summary>
    bool TrackStock,
    decimal? OpeningQuantity,
    decimal? PurchasePriceExVat,
    PaydayProductRole Role,
    string? Unit,
    string? Category);
