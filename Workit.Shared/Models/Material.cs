namespace Workit.Shared.Models;

public sealed class Material
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public Guid   CompanyId   { get; set; }

    /// <summary>Product name, e.g. "N1XE-U 5G 16 Cu 1kV Aflstrengur"</summary>
    public string Name        { get; set; } = string.Empty;

    /// <summary>Supplier SKU / internal code, e.g. "N1XECU015G016"</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Top-level category, e.g. "Rafstrengir"</summary>
    public string Category    { get; set; } = string.Empty;

    /// <summary>Unit of measure: m., stk., pk., rúll., box</summary>
    public string Unit        { get; set; } = "stk.";

    /// <summary>Current stock quantity (deducted automatically when usage is logged)</summary>
    public decimal Quantity   { get; set; }

    /// <summary>Purchase price (what we pay the vendor), excl. VAT (ISK). Auto-updated from invoices.</summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>Markup multiplier applied to PurchasePrice to derive UnitPrice. Default 1.5 = 50% markup.</summary>
    public decimal MarkupFactor  { get; set; } = 1.5m;

    /// <summary>Sale price excl. VAT (ISK) — what we charge on jobs. = PurchasePrice × MarkupFactor, or set manually.</summary>
    public decimal UnitPrice  { get; set; }

    /// <summary>VAT rate in percent, e.g. 24.0</summary>
    public decimal VatRate    { get; set; } = 24.0m;

    public string Description { get; set; } = string.Empty;

    public bool IsActive      { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
