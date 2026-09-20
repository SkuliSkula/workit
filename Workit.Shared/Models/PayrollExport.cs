namespace Workit.Shared.Models;

/// <summary>
/// One upload of a period's hours to Payday payroll. Kept so the owner can see
/// what was sent, by whom, and what Payday made of it; a period may be sent
/// again after corrections and each send is its own row.
/// </summary>
public sealed class PayrollExport
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public int  Year      { get; set; }
    public int  Month     { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid?  SentByUserId { get; set; }
    public string SentByName   { get; set; } = string.Empty;
    /// <summary>Employees in the upload and how many Payday recognised.</summary>
    public int EmployeesSent { get; set; }
    public int EmployeesRead { get; set; }
    /// <summary>Comma-separated SSNs Payday did not recognise, empty when all were read.</summary>
    public string SsnsNotOnRecord { get; set; } = string.Empty;
    public Guid? PaydayPayoutId { get; set; }
    /// <summary>The JSON body as sent, for the audit trail.</summary>
    public string PayloadJson { get; set; } = string.Empty;
}

/// <summary>One employee's totals for a period, as the Payroll page shows them before sending.</summary>
public sealed record PayrollPreviewRow(
    Guid EmployeeId, string EmployeeName, string Ssn, bool HasSsn,
    decimal RegularHours, decimal OvertimeHours, int DrivingUnits, int EntryCount);

public sealed record PayrollPreview(
    int Year, int Month,
    string RegularItemName, string OvertimeItemName, string DrivingItemName,
    List<PayrollPreviewRow> Rows,
    /// <summary>The most recent export of this period, if any.</summary>
    PayrollExport? LastExport);

public sealed record UpdatePayrollItemNamesRequest(string RegularItemName, string OvertimeItemName, string DrivingItemName);
