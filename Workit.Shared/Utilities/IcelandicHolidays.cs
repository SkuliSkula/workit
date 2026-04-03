namespace Workit.Shared.Utilities;

public sealed record PublicHoliday(DateOnly Date, string Name, string NameIcelandic, bool IsHalfDay);

public static class IcelandicHolidays
{
    /// <summary>
    /// Returns all Icelandic public holidays for the given year.
    /// </summary>
    public static List<PublicHoliday> GetHolidays(int year)
    {
        var easter = ComputeEasterSunday(year);
        var holidays = new List<PublicHoliday>
        {
            // Fixed-date holidays
            new(new DateOnly(year, 1, 1), "New Year's Day", "Nýársdagur", false),

            new(new DateOnly(year, 5, 1), "Labour Day", "Verkalýðsdagurinn", false),
            new(new DateOnly(year, 6, 17), "National Day", "Þjóðhátíðardagurinn", false),
            new(ComputeFirstMondayOfAugust(year), "Commerce Day", "Frídagur verslunarmanna", false),
            new(new DateOnly(year, 12, 24), "Christmas Eve", "Aðfangadagur", true),
            new(new DateOnly(year, 12, 25), "Christmas Day", "Jóladagur", false),
            new(new DateOnly(year, 12, 26), "2nd Day of Christmas", "Annar í jólum", false),
            new(new DateOnly(year, 12, 31), "New Year's Eve", "Gamlársdagur", true),

            // Easter-based (movable) holidays
            new(easter.AddDays(-3), "Maundy Thursday", "Skírdagur", false),
            new(easter.AddDays(-2), "Good Friday", "Föstudagurinn langi", false),
            new(easter, "Easter Sunday", "Páskadagur", false),
            new(easter.AddDays(1), "Easter Monday", "Annar í páskum", false),
            new(ComputeFirstDayOfSummer(year), "First Day of Summer", "Sumardagurinn fyrsti", false),
            new(easter.AddDays(39), "Ascension Day", "Uppstigningardagur", false),
            new(easter.AddDays(49), "Whit Sunday", "Hvítasunnudagur", false),
            new(easter.AddDays(50), "Whit Monday", "Annar í hvítasunnu", false),
        };

        holidays.Sort((a, b) => a.Date.CompareTo(b.Date));
        return holidays;
    }

    /// <summary>
    /// Returns all Icelandic public holidays that fall within the given month.
    /// </summary>
    public static List<PublicHoliday> GetHolidaysInMonth(int year, int month)
    {
        return GetHolidays(year)
            .Where(h => h.Date.Month == month)
            .ToList();
    }

    /// <summary>
    /// Returns the number of working days in the given month.
    /// Weekdays (Mon-Fri) minus full holidays; half-day holidays count as 0.5.
    /// </summary>
    public static decimal GetWorkingDaysInMonth(int year, int month, decimal standardHoursPerDay = 8m)
    {
        var holidays = GetHolidaysInMonth(year, month);
        var holidayLookup = holidays.ToDictionary(h => h.Date);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var workingDays = 0m;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var dayOfWeek = date.DayOfWeek;

            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
                continue;

            if (holidayLookup.TryGetValue(date, out var holiday))
            {
                if (holiday.IsHalfDay)
                    workingDays += 0.5m;
                // Full holiday: add nothing
            }
            else
            {
                workingDays += 1m;
            }
        }

        return workingDays;
    }

    /// <summary>
    /// Returns the total work-duty hours for the given month.
    /// </summary>
    public static decimal GetWorkDutyHours(int year, int month, decimal standardHoursPerDay = 8m)
    {
        return GetWorkingDaysInMonth(year, month, standardHoursPerDay) * standardHoursPerDay;
    }

    /// <summary>
    /// Computes Easter Sunday using the Anonymous Gregorian algorithm (Computus).
    /// </summary>
    private static DateOnly ComputeEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// First Monday of August.
    /// </summary>
    private static DateOnly ComputeFirstMondayOfAugust(int year)
    {
        var date = new DateOnly(year, 8, 1);
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    /// <summary>
    /// First Day of Summer: first Thursday on or after April 19.
    /// </summary>
    private static DateOnly ComputeFirstDayOfSummer(int year)
    {
        var date = new DateOnly(year, 4, 19);
        while (date.DayOfWeek != DayOfWeek.Thursday)
            date = date.AddDays(1);
        return date;
    }
}
