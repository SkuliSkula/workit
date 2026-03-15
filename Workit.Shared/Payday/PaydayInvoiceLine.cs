namespace Workit.Shared.Payday;

public sealed class PaydayInvoiceLine
{
    public Guid     Id                            { get; set; }
    // API returns "invoiceId" (camelCase) — resolved via case-insensitive JsonOptions
    public Guid     InvoiceId                     { get; set; }
    public int      Position                      { get; set; }
    public string   Description                   { get; set; } = string.Empty;
    public string?  Comment                       { get; set; }
    public decimal  Quantity                      { get; set; }
    public decimal  UnitPriceExcludingVat         { get; set; }
    public decimal  UnitPriceIncludingVat         { get; set; }
    public decimal? ForeignUnitPriceExcludingVat  { get; set; }
    public decimal? ForeignUnitPriceIncludingVat  { get; set; }
    public decimal  VatPercentage                 { get; set; }
    // API can return null for discountPercentage
    public decimal? DiscountPercentage            { get; set; }
    public string?  Sku                           { get; set; }
    public Guid?    ProductId                     { get; set; }
    public DateTime Created                       { get; set; }
}
