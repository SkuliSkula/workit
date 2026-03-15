namespace Workit.Shared.Payday;

public sealed class PaydayInvoicePayment
{
    public Guid      Id            { get; set; }
    public Guid      PaymentTypeId { get; set; }
    public DateTime  PaymentDate   { get; set; }
    public decimal   Amount        { get; set; }
    public string?   CurrencyCode  { get; set; }
}
