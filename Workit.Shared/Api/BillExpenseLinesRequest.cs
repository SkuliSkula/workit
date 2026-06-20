namespace Workit.Shared.Api;

/// <summary>
/// Request to bill (partially or fully) one or more expense lines onto a customer invoice.
/// Each entry creates an ExpenseLineBilling record, tagged with the invoice number for traceback.
/// </summary>
public sealed class BillExpenseLinesRequest
{
    public int? PaydayInvoiceNumber { get; set; }
    public List<ExpenseLineBillingEntry> Lines { get; set; } = [];
}

public sealed class ExpenseLineBillingEntry
{
    public Guid     ExpenseLineId         { get; set; }
    public Guid?    JobId                 { get; set; }
    public decimal  Quantity              { get; set; }
    public decimal  SalePriceExcludingVat { get; set; }
    public decimal  VatPercentage         { get; set; }
    public Guid?    InvoiceLineId         { get; set; }
}
