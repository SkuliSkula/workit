namespace Workit.Shared.Models;

public sealed class Expense
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }

    // Creditor stored inline (Payday-style, no FK)
    public Guid?   CreditorPaydayId { get; set; }
    public string? CreditorSsn      { get; set; }
    public string? CreditorName     { get; set; }

    // Payment type
    public Guid?   PaymentTypePaydayId { get; set; }
    public string? PaymentTypeName     { get; set; }

    /// <summary>DRAFT | UNPAID | PAID</summary>
    public string Status { get; set; } = "DRAFT";

    public DateTime? Date         { get; set; }
    public DateTime? DueDate      { get; set; }
    public DateTime? FinalDueDate { get; set; }
    public DateTime? PaidDate     { get; set; }

    public string? Reference  { get; set; }
    public bool    Deductible { get; set; }
    public string? Comments   { get; set; }
    public string? Voucher    { get; set; }

    public decimal AmountExcludingVat { get; set; }
    public decimal AmountIncludingVat { get; set; }
    public decimal AmountVat          { get; set; }

    /// <summary>Optional link to a Workit job.</summary>
    public Guid? JobId { get; set; }

    /// <summary>Where this record came from.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday expense id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ExpenseLine> Lines { get; set; } = [];
}
