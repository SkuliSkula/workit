namespace Workit.Shared.Models;

public sealed class InvoicePayment
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Payday payment type id.</summary>
    public Guid?   PaymentTypePaydayId { get; set; }
    public string? PaymentTypeName     { get; set; }

    public DateTime PaymentDate  { get; set; }
    public decimal  Amount       { get; set; }
    public string?  CurrencyCode { get; set; }

    /// <summary>Payday payment id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
