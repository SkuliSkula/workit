namespace Workit.Shared.Payday;

/// <summary>
/// PUT /products/{id}. Deliberately has no quantity: Payday books a quantity on an
/// update as a stock correction ("Birgðaleiðrétting", docs/payday-spike.md §2), and a
/// price change must never move stock. Null fields are left out of the request.
/// </summary>
public sealed class UpdateProductRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Sku { get; set; }
    public decimal SalesUnitPriceExcludingVAT { get; set; }
    public decimal VatPercentage { get; set; }
    public Guid? SalesLedgerAccountId { get; set; }
    public List<string>? Tags { get; set; }
    public bool Archived { get; set; }
}
