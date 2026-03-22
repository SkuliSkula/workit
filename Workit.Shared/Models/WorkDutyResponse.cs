namespace Workit.Shared.Models;

public sealed class WorkDutyResponse
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalCalendarDays { get; set; }
    public int WeekdaysInMonth { get; set; }
    public int FullHolidays { get; set; }
    public int HalfHolidays { get; set; }
    public decimal WorkDutyHours { get; set; }
    public decimal HoursWorked { get; set; }
    public decimal HoursRemaining { get; set; }
    public decimal CompletionPercentage { get; set; }
    public List<HolidayInfo> Holidays { get; set; } = [];
}

public sealed class HolidayInfo
{
    public string Date { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHalfDay { get; set; }
}
