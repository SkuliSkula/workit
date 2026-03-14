namespace Workit.Shared.Payday;

public sealed class TimesheetEntry
{
    public required string Ssn { get; set; }
    public string? Name { get; set; }
    public List<TimesheetItem> Items { get; set; } = [];
}
