namespace Workit.Shared.Models;

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }

    // Customer stored inline (Payday-style, no FK)
    public Guid?   CustomerPaydayId { get; set; }
    public string? CustomerSsn      { get; set; }
    public string? CustomerName     { get; set; }

    // Payor when it differs from the customer
    public Guid?   PayorPaydayId { get; set; }
    public string? PayorSsn      { get; set; }
    public string? PayorName     { get; set; }

    public int?    Number { get; set; }

    /// <summary>DRAFT | SENT | PAID | CANCELLED | CREDIT | DELETED</summary>
    public string  Status { get; set; } = "DRAFT";

    public string? Description { get; set; }
    public string? Reference   { get; set; }

    public string  CurrencyCode { get; set; } = "ISK";
    public decimal CurrencyRate { get; set; } = 1m;

    public DateTime? InvoiceDate        { get; set; }
    public DateTime? DueDate            { get; set; }
    public DateTime? FinalDueDate       { get; set; }
    public DateTime? SentDate           { get; set; }
    public DateTime? PaidDate           { get; set; }
    public DateTime? CancelDate         { get; set; }
    public DateTime? CancelledDate      { get; set; }
    public DateTime? CreditDate         { get; set; }
    public DateTime? RefundDate         { get; set; }
    public DateTime? ClaimCreated       { get; set; }
    public DateTime? ClaimFinalDueDate  { get; set; }
    public DateTime? ClaimCancelledDate { get; set; }
    public bool      ClaimCancelled     { get; set; }

    public decimal  AmountExcludingVat        { get; set; }
    public decimal  AmountIncludingVat        { get; set; }
    public decimal  AmountVat                 { get; set; }
    public decimal? ForeignAmountExcludingVat { get; set; }
    public decimal? ForeignAmountIncludingVat { get; set; }
    public decimal? ForeignAmountVat          { get; set; }

    public string? VatNumber                { get; set; }
    public bool    CreateClaim              { get; set; }
    public bool    CreateElectronicInvoice  { get; set; }
    public string? ElectronicInvoicePartyId { get; set; }
    public bool    SendEmail                { get; set; }
    public decimal DefaultInterest          { get; set; }
    public decimal CapitalGainsTax          { get; set; }
    public string? AccountingCost           { get; set; }
    public string? Ocr                      { get; set; }

    /// <summary>Where this record came from.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday invoice id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<InvoiceLine>    Lines    { get; set; } = [];
    public List<InvoicePayment> Payments { get; set; } = [];
}
