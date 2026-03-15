namespace Workit.Shared.Payday;

public sealed class CreateInvoiceRequest
{
    /// <summary>Required.</summary>
    public required InvoiceCustomerRef Customer          { get; set; }
    /// <summary>Optional – defaults to Customer if omitted.</summary>
    public InvoiceCustomerRef?          Payor            { get; set; }
    /// <summary>Optional. Max 1024 characters.</summary>
    public string?                      Description      { get; set; }
    /// <summary>Required. Format: YYYY-MM-DD</summary>
    public required string              InvoiceDate      { get; set; }
    /// <summary>Required. Format: YYYY-MM-DD</summary>
    public required string              DueDate          { get; set; }
    /// <summary>Required. Format: YYYY-MM-DD</summary>
    public required string              FinalDueDate     { get; set; }
    /// <summary>Required. Three-digit currency code, e.g. "ISK".</summary>
    public required string              CurrencyCode     { get; set; }
    /// <summary>Create a claim in the customer's internet bank.</summary>
    public bool?                        CreateClaim               { get; set; }
    /// <summary>If not specified, the customer setting is used.</summary>
    public bool?                        CreateElectronicInvoice   { get; set; }
    public string?                      ElectronicInvoicePartyId  { get; set; }
    public string?                      AccountingCost            { get; set; }
    public string?                      Reference                 { get; set; }
    /// <summary>Send the invoice by email.</summary>
    public bool?                        SendEmail                 { get; set; }
    /// <summary>Mark as paid immediately. Use Payments array for multiple payments.</summary>
    public string?                      PaidDate                  { get; set; }
    /// <summary>Payment type ID when marking as paid. Reference: Sales → Payment Types.</summary>
    public Guid?                        PaymentType               { get; set; }
    /// <summary>DRAFT | SENT (default when omitted = SENT)</summary>
    public string?                      Status                    { get; set; }
    /// <summary>Required. At least one line.</summary>
    public required List<CreateInvoiceLineRequest> Lines         { get; set; }
    /// <summary>Optional. Use instead of PaidDate + PaymentType for multiple payments.</summary>
    public List<CreateInvoicePaymentRequest>?      Payments      { get; set; }
}
