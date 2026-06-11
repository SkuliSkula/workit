namespace Workit.Shared.Models;

public sealed class InvoiceLine
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid CompanyId { get; set; }

    public int     Position                     { get; set; }
    public string  Description                  { get; set; } = string.Empty;
    public string? Comment                      { get; set; }
    public decimal Quantity                     { get; set; }
    public decimal UnitPriceExcludingVat        { get; set; }
    public decimal UnitPriceIncludingVat        { get; set; }
    public decimal? ForeignUnitPriceExcludingVat { get; set; }
    public decimal? ForeignUnitPriceIncludingVat { get; set; }
    public decimal  VatPercentage               { get; set; }
    public decimal? DiscountPercentage          { get; set; }
    public string?  Sku                         { get; set; }

    /// <summary>Payday product id, when synced from Payday.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Payday line id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
