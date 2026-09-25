using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Utilities;

namespace Workit.Api.Endpoints;

/// <summary>
/// How much of the month's work duty an absence covers. Being off sick or on
/// holiday does not leave the month short.
///
/// Absence is recorded in hours, because a day is not all-or-nothing: someone
/// can work the morning, go home ill, and register four worked hours and four
/// sick ones. Two rules keep the sums honest:
///
///   • a day of absence never exceeds the duty that day holds — a standard day
///     on a weekday, half on a half-holiday, nothing on a weekend or full
///     holiday, since there was no duty to be absent from;
///   • absence only fills what the hours worked left. Work four and register
///     four sick and the day counts eight; work eight and register eight sick
///     and it still counts eight, not sixteen.
/// </summary>
internal static class AbsenceDuty
{
    /// <summary>
    /// Unpaid leave is the one kind that does not count: the hours were neither
    /// worked nor paid, so the duty stays unmet.
    /// </summary>
    internal static bool CountsTowardDuty(AbsenceType type) => type != AbsenceType.UnpaidLeave;

    /// <summary>
    /// Absence hours inside the month, from approved requests only.
    /// <paramref name="workedByDay"/> is the employee's logged hours per day, so
    /// absence can fill the rest of the day and no more.
    /// </summary>
    internal static decimal HoursInMonth(
        IEnumerable<AbsenceRequest> requests,
        int year,
        int month,
        decimal standardHoursPerDay,
        IReadOnlyDictionary<DateOnly, decimal>? workedByDay = null)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var holidays = IcelandicHolidays.GetHolidaysInMonth(year, month)
            .ToDictionary(h => h.Date, h => h.IsHalfDay);

        // The most any one day can contribute, whatever was registered on it.
        var bestByDay = new Dictionary<DateOnly, decimal>();

        foreach (var request in requests)
        {
            if (request.Status != AbsenceStatus.Approved) continue;
            if (!CountsTowardDuty(request.Type)) continue;

            var from = request.StartDate > monthStart ? request.StartDate : monthStart;
            var to   = request.EndDate   < monthEnd   ? request.EndDate   : monthEnd;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var dayDuty = DayValue(day, holidays) * standardHoursPerDay;
                if (dayDuty <= 0) continue;

                var registered = request.HoursPerDay > 0 ? request.HoursPerDay : standardHoursPerDay;
                var hours = Math.Min(registered, dayDuty);

                // Overlapping requests never pay a day twice: the day is worth
                // the largest single claim on it, not their sum.
                bestByDay[day] = bestByDay.TryGetValue(day, out var already) ? Math.Max(already, hours) : hours;
            }
        }

        var total = 0m;
        foreach (var (day, hours) in bestByDay)
        {
            var dayDuty = DayValue(day, holidays) * standardHoursPerDay;
            var worked  = workedByDay is not null && workedByDay.TryGetValue(day, out var w) ? w : 0m;
            var room    = dayDuty - worked;
            if (room <= 0) continue;              // the day's duty is already met by work
            total += Math.Min(hours, room);
        }

        return total;
    }

    /// <summary>
    /// Refuses hours that would overrun a day already covered by absence. Work
    /// four and go home ill for four and the day is full but fine; eight of each
    /// is someone registering the same day twice. Returns the message to show,
    /// or null when the day still has room.
    /// </summary>
    internal static async Task<string?> DayOverflowAsync(
        WorkitDbContext db, Guid companyId, Guid employeeId, DateOnly day, decimal hours, Guid? ignoreEntryId, CancellationToken ct)
    {
        var absences = await db.AbsenceRequests
            .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId
                     && a.Status == AbsenceStatus.Approved
                     && a.StartDate <= day && a.EndDate >= day)
            .ToListAsync(ct);
        if (absences.Count == 0) return null;

        var standardDay = await db.Companies.Where(c => c.Id == companyId)
            .Select(c => (decimal?)c.StandardHoursPerDay).FirstOrDefaultAsync(ct) ?? 8m;
        if (standardDay <= 0) standardDay = 8m;

        var holidays = IcelandicHolidays.GetHolidaysInMonth(day.Year, day.Month)
            .ToDictionary(h => h.Date, h => h.IsHalfDay);
        var dayDuty = DayValue(day, holidays) * standardDay;
        if (dayDuty <= 0) return null;   // a weekend or holiday has no duty to overrun

        var counted = absences
            .Where(a => CountsTowardDuty(a.Type))
            .Select(a => Math.Min(a.HoursPerDay > 0 ? a.HoursPerDay : standardDay, dayDuty))
            .DefaultIfEmpty(0m)
            .Max();
        if (counted <= 0) return null;

        var alreadyWorked = await db.TimeEntries
            .Where(t => t.CompanyId == companyId && t.EmployeeId == employeeId && t.WorkDate == day
                     && (ignoreEntryId == null || t.Id != ignoreEntryId))
            .SumAsync(t => t.Hours, ct);

        var room = dayDuty - counted - alreadyWorked;
        if (hours <= room) return null;

        var type = absences.First(a => CountsTowardDuty(a.Type)).Type;
        return room <= 0
            ? $"You already have {counted:0.##} h of {Describe(type)} registered on {day:d MMM yyyy} — that day is full."
            : $"You already have {counted:0.##} h of {Describe(type)} registered on {day:d MMM yyyy}, so only {room:0.##} h more fit that day.";
    }

    private static string Describe(AbsenceType type) => type switch
    {
        AbsenceType.SickLeave      => "sick leave",
        AbsenceType.SickChildLeave => "sick child leave",
        AbsenceType.Vacation       => "holiday",
        AbsenceType.ParentalLeave  => "parental leave",
        AbsenceType.WorkInjury     => "work injury leave",
        _                          => "absence",
    };

    /// <summary>What one day is worth as duty: 1, a half-holiday 0.5, otherwise 0.</summary>
    private static decimal DayValue(DateOnly day, IReadOnlyDictionary<DateOnly, bool> holidays)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0m;
        if (holidays.TryGetValue(day, out var isHalfDay)) return isHalfDay ? 0.5m : 0m;
        return 1m;
    }
}
