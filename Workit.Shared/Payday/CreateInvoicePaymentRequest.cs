namespace Workit.Shared.Payday;

public sealed class CreateInvoicePaymentRequest
{
    /// <summary>Required. Reference: Sales → Payment Types</summary>
    public required string PaymentTypeId { get; set; }
    /// <summary>Required. Format: YYYY-MM-DD</summary>
    public required string PaymentDate   { get; set; }
    /// <summary>Required. Amount in ISK.</summary>
    public required decimal Amount       { get; set; }
}
