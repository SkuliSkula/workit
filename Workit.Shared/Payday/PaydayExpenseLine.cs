namespace Workit.Shared.Payday;

public sealed class PaydayExpenseLine
{
    public Guid     Id                    { get; set; }
    public Guid     ExpenseId             { get; set; }
    public string   Description           { get; set; } = string.Empty;
    public int      Quantity              { get; set; }
    public decimal  UnitPriceExcludingVat { get; set; }
    public decimal  UnitPriceIncludingVat { get; set; }
    public decimal  VatPercentage         { get; set; }
    public decimal? DiscountPercentage    { get; set; }
    public Guid?    AccountId             { get; set; }
    public DateTime Created               { get; set; }
}
