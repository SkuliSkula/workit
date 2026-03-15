using System.Text.Json.Serialization;

namespace Workit.Shared.Payday;

/// <summary>
/// Flexible update request supporting 6 modes:
/// - Mark as paid:        Status = "PAID", PaidDate, PaymentType
/// - Resend email:        Status = "RESEND"
/// - Cancel claim:        Status = "CANCEL_CLAIM"
/// - Payment extension:   FinalDueDate
/// - Cancel invoice:      Status = "CANCELLED"
/// - Update draft:        Full update with Lines etc.
/// Only include the properties relevant to the operation.
/// </summary>
public sealed class UpdateInvoiceRequest
{
    /// <summary>PAID | CANCELLED | RESEND | CANCEL_CLAIM | DRAFT</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  Status                   { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  PaidDate                 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid?    PaymentType              { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  FinalDueDate             { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  DueDate                  { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  Description              { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?  CurrencyCode             { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool?    CreateClaim              { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool?    CreateElectronicInvoice  { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool?    SendEmail                { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CreateInvoiceLineRequest>?   Lines    { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CreateInvoicePaymentRequest>? Payments { get; set; }
}
