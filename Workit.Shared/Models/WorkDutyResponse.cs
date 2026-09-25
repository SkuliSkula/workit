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

    /// <summary>
    /// Approved absence in the month, counted toward the duty: each absent
    /// working day is worth a standard day, half on a half-holiday, nothing on
    /// a weekend or full holiday. Unpaid leave is not counted — those hours
    /// were neither worked nor paid.
    /// </summary>
    public decimal AbsenceHours { get; set; }

    /// <summary>Worked plus counted absence — what the month's duty is measured against.</summary>
    public decimal HoursCounted { get; set; }
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
