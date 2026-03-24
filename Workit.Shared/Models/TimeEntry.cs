namespace Workit.Shared.Models;

public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid JobId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    public int DrivingUnits { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>Whether this entry has been included in a Payday invoice.</summary>
    public bool IsInvoiced { get; set; }

    /// <summary>When this entry was marked as invoiced (UTC).</summary>
    public DateTimeOffset? InvoicedAt { get; set; }

    /// <summary>The Payday invoice number this entry was billed on.</summary>
    public int? PaydayInvoiceNumber { get; set; }
}
