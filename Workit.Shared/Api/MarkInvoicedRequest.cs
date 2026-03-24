namespace Workit.Shared.Api;

/// <summary>
/// Request to mark time entries or material usages as invoiced,
/// linking them to a specific Payday invoice number.
/// </summary>
public sealed class MarkInvoicedRequest
{
    public List<Guid> Ids { get; set; } = [];
    public int? PaydayInvoiceNumber { get; set; }
}
