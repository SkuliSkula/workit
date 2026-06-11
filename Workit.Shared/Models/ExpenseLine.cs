namespace Workit.Shared.Models;

public sealed class ExpenseLine
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public Guid CompanyId { get; set; }

    public string  Description           { get; set; } = string.Empty;
    public decimal Quantity              { get; set; }
    public decimal UnitPriceExcludingVat { get; set; }
    public decimal UnitPriceIncludingVat { get; set; }
    public decimal VatPercentage         { get; set; }
    public decimal? DiscountPercentage   { get; set; }

    /// <summary>Payday accounting account id.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Payday line id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
