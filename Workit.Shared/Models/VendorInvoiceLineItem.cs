namespace Workit.Shared.Models;

public sealed class VendorInvoiceLineItem
{
    public Guid   Id                  { get; set; } = Guid.NewGuid();
    public Guid   CompanyId           { get; set; }
    public Guid   InvoiceId           { get; set; }
    public string ProductCode         { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public decimal Quantity           { get; set; }
    public string Unit                { get; set; } = "stk.";
    /// <summary>Vendor list price before discount.</summary>
    public decimal ListPrice          { get; set; }
    /// <summary>Trade discount percentage (e.g. 10 = 10%).</summary>
    public decimal DiscountPercent    { get; set; }
    /// <summary>Actual purchase price = ListPrice × (1 − DiscountPercent/100).</summary>
    public decimal PurchasePrice      { get; set; }
    public bool   IsImported          { get; set; }
    /// <summary>Set once the line item has been matched to a Material record.</summary>
    public Guid?  ImportedMaterialId  { get; set; }
}
