namespace Workit.Shared.Payday;

public sealed class CreateInvoiceLineRequest
{
    /// <summary>Required.</summary>
    public required string  Description           { get; set; }
    public string?          Comment               { get; set; }
    /// <summary>Required.</summary>
    public required int     Quantity              { get; set; }
    /// <summary>If set, UnitPriceIncludingVat is ignored.</summary>
    public decimal?         UnitPriceExcludingVat { get; set; }
    /// <summary>If set, UnitPriceExcludingVat should be empty.</summary>
    public decimal?         UnitPriceIncludingVat { get; set; }
    /// <summary>Required.</summary>
    public required decimal VatPercentage         { get; set; }
    /// <summary>Required.</summary>
    public required decimal DiscountPercentage    { get; set; }
    public string?          Sku                   { get; set; }
    public Guid?            ProductId             { get; set; }
}
