namespace Workit.Shared.Models;

public sealed class VendorInvoice
{
    public Guid   Id                    { get; set; } = Guid.NewGuid();
    public Guid   CompanyId             { get; set; }
    public string InvoiceNumber         { get; set; } = string.Empty;
    public string VendorName            { get; set; } = string.Empty;
    public string VendorEmail           { get; set; } = string.Empty;
    public DateOnly InvoiceDate         { get; set; }
    public decimal SubtotalExVat        { get; set; }
    public decimal VatAmount            { get; set; }
    public decimal TotalInclVat         { get; set; }
    public string SourceEmailSubject    { get; set; } = string.Empty;
    /// <summary>IMAP Message-ID header — used to prevent duplicate imports.</summary>
    public string SourceEmailMessageId  { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt    { get; set; } = DateTimeOffset.UtcNow;

    public List<VendorInvoiceLineItem> LineItems { get; set; } = [];
}
