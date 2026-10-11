namespace Workit.Shared.Models;

/// <summary>
/// A single partial billing of an expense line onto a customer invoice.
/// Multiple billings per line support charging part of a purchased quantity now
/// and the remainder later (e.g. buy 100 m cable, bill 50 m on one invoice, 50 m on another).
/// </summary>
public sealed class ExpenseLineBilling
{
    public Guid Id            { get; set; } = Guid.NewGuid();
    public Guid CompanyId     { get; set; }
    public Guid ExpenseLineId { get; set; }

    /// <summary>The job this billing was raised for.</summary>
    public Guid? JobId { get; set; }

    /// <summary>Quantity billed in this billing (≤ the line's remaining quantity).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Sale price excl. VAT per unit used for this billing.</summary>
    public decimal SalePriceExcludingVat { get; set; }

    public decimal VatPercentage { get; set; }

    /// <summary>The Payday invoice number this billing was placed on — for traceback.</summary>
    public int? PaydayInvoiceNumber { get; set; }
    /// <summary>
    /// The Payday invoice this was put on, by id. A draft has an id from the moment
    /// it is created but no number until it is issued, so the number alone left a
    /// draft's records pointing at nothing — and nothing could release them when
    /// the draft was deleted or cancelled.
    /// </summary>
    public Guid? PaydayInvoiceId { get; set; }

    /// <summary>The corresponding Workit invoice line id, when available.</summary>
    public Guid? InvoiceLineId { get; set; }

    public DateTimeOffset BilledAt { get; set; } = DateTimeOffset.UtcNow;
}
