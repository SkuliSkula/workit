namespace Workit.Shared.Models;

public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid JobId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string Notes { get; set; } = string.Empty;
}
