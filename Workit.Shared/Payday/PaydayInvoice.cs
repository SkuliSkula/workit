namespace Workit.Shared.Payday;

public sealed class PaydayInvoice
{
    public Guid      Id                     { get; set; }
    public PaydayCustomer? Customer         { get; set; }
    public PaydayCustomer? Payor            { get; set; }
    public string?   Description            { get; set; }
    public string?   Reference              { get; set; }
    public int?      Number                 { get; set; }

    /// <summary>DRAFT | SENT | PAID | CANCELLED | CREDIT | DELETED</summary>
    public string    Status                 { get; set; } = string.Empty;

    public DateTime? Created               { get; set; }
    public DateTime? ClaimCreated          { get; set; }
    public DateTime? CancelDate            { get; set; }
    public DateTime? ClaimFinalDueDate     { get; set; }
    public DateTime? InvoiceDate           { get; set; }
    public DateTime? DueDate               { get; set; }
    public DateTime? FinalDueDate          { get; set; }
    public DateTime? PaidDate              { get; set; }
    public DateTime? CancelledDate         { get; set; }
    public DateTime? RefundDate            { get; set; }
    public DateTime? CreditDate            { get; set; }
    public DateTime? SentDate              { get; set; }
    public DateTime? ClaimCancelledDate    { get; set; }
    public bool      ClaimCancelled        { get; set; }

    public decimal   AmountExcludingVat    { get; set; }
    public decimal   AmountIncludingVat    { get; set; }
    public decimal   AmountVat             { get; set; }
    public decimal?  ForeignAmountExcludingVat { get; set; }
    public decimal?  ForeignAmountIncludingVat { get; set; }
    public decimal?  ForeignAmountVat      { get; set; }

    public string    CurrencyCode          { get; set; } = string.Empty;
    public decimal   CurrencyRate          { get; set; }
    public string?   VatNumber             { get; set; }
    public bool      CreateClaim           { get; set; }
    public bool      CreateElectronicInvoice { get; set; }
    public string?   ElectronicInvoicePartyId { get; set; }
    public string?   AccountingCost        { get; set; }
    public string?   Ocr                   { get; set; }
    public bool      SendEmail             { get; set; }
    public decimal   DefaultInterest       { get; set; }
    public decimal   CapitalGainsTax       { get; set; }

    // null when fetched without ?include=lines
    public List<PaydayInvoiceLine>?    Lines    { get; set; }
    public List<PaydayInvoicePayment>? Payments { get; set; }
}
