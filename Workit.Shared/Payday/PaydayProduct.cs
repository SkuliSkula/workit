namespace Workit.Shared.Payday;

/// <summary>
/// A Payday product as the API returns it. A product is any price-list line —
/// a cable or "Vinna, klst." look the same; only stock products carry a
/// <see cref="Quantity"/> (null for services). See docs/payday-spike.md.
/// </summary>
public sealed class PaydayProduct
{
    public Guid     Id                         { get; set; }
    public string?  Name                       { get; set; }
    public string?  Description                { get; set; }
    public string?  Sku                        { get; set; }
    /// <summary>Stock on hand; null for a product that is not stock-tracked.</summary>
    public decimal? Quantity                   { get; set; }
    public decimal? SalesUnitPriceExcludingVAT { get; set; }
    public decimal? SalesUnitPriceIncludingVAT { get; set; }
    public Guid?    SalesLedgerAccountId       { get; set; }
    public decimal? VatPercentage              { get; set; }
    public List<string>? Tags                  { get; set; }
    public bool     Archived                   { get; set; }
    public DateTimeOffset? Created             { get; set; }
    public string?  SupplierProductNumber      { get; set; }
}

public sealed class PaydayProductsResponse
{
    public List<PaydayProduct> Products { get; set; } = [];
    public int PerPage { get; set; }
    public int Total   { get; set; }
    public int Page    { get; set; }
    public int Pages   { get; set; }
}

/// <summary>One entry in a product's stock ledger (GET /products/{id}/movements).</summary>
public sealed class PaydayProductMovement
{
    public Guid     Id                            { get; set; }
    public Guid     ProductId                     { get; set; }
    public DateTime Date                          { get; set; }
    public string?  Description                   { get; set; }
    public decimal  ChangeInQuantity              { get; set; }
    public decimal? PurchaseUnitPriceExcludingVAT { get; set; }
    public decimal? SalesUnitPriceExcludingVAT    { get; set; }
    public bool     Reserved                      { get; set; }
    public decimal? QuantityAfterChange           { get; set; }
    public DateTimeOffset? Created                { get; set; }
}

public sealed class PaydayProductMovementsResponse
{
    public List<PaydayProductMovement> ProductMovements { get; set; } = [];
    public int PerPage { get; set; }
    public int Total   { get; set; }
    public int Page    { get; set; }
    public int Pages   { get; set; }
}

/// <summary>Sales / inventory / COGS ledger account as listed under /products/*LedgerAccounts.</summary>
public sealed class PaydayLedgerAccount
{
    public Guid    Id       { get; set; }
    public string? Code     { get; set; }
    public string? Name     { get; set; }
    public string? Type     { get; set; }
    public bool    Archived { get; set; }
}
