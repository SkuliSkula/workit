namespace Workit.Shared.Api;

/// <summary>
/// Request to reset invoicing status for all time entries or material usages
/// that were billed on a specific Payday invoice number.
/// </summary>
public sealed class MarkUninvoicedRequest
{
    public int PaydayInvoiceNumber { get; set; }
}
