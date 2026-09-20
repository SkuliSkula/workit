namespace Workit.Shared.Payday;

/// <summary>What Payday answers to POST payroll/upload/timesheet (see docs/payday-spike.md, finding 7).</summary>
public sealed class TimesheetUploadResult
{
    public Guid PayoutId { get; set; }
    public int  TotalEmployeesInFile { get; set; }
    public int  TotalEmployeesRead   { get; set; }
    /// <summary>SSNs Payday has no payroll employee for; null when all were read.</summary>
    public List<string>? EmployeeSSNsNotOnRecord { get; set; }
}
