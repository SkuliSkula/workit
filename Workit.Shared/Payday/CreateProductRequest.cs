namespace Workit.Shared.Payday;

/// <summary>
/// POST /products. Used once per material by the Phase 4 migration; Workit
/// otherwise never creates products. The purchase fields register an opening
/// stock movement ("Upphafsbirgðastaða") — see docs/payday-spike.md.
/// </summary>
public sealed class CreateProductRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Sku { get; set; }
    public decimal? Quantity { get; set; }
    public decimal SalesUnitPriceExcludingVAT { get; set; }
    public decimal VatPercentage { get; set; }
    public Guid SalesLedgerAccountId { get; set; }
    public string? PurchaseDate { get; set; }
    public decimal? PurchaseUnitPriceExcludingVAT { get; set; }
    public List<string>? Tags { get; set; }
    public bool Archived { get; set; }
}
