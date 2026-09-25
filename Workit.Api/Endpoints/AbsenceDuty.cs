using Workit.Shared.Models;
using Workit.Shared.Utilities;

namespace Workit.Api.Endpoints;

/// <summary>
/// How much of the month's work duty an absence covers. Being off sick or on
/// holiday does not leave the month short: each absent working day counts as a
/// standard day — which is also the cap, so two days off is two days, never the
/// 16 hours someone might otherwise log.
///
/// The day is worth exactly what the duty calculation says it is worth: a full
/// day on a normal weekday, half on a half-holiday, and nothing on a weekend or
/// a full holiday, because there was no duty to be absent from.
/// </summary>
internal static class AbsenceDuty
{
    /// <summary>
    /// Unpaid leave is the one kind that does not count: the hours were neither
    /// worked nor paid, so the duty stays unmet.
    /// </summary>
    internal static bool CountsTowardDuty(AbsenceType type) => type != AbsenceType.UnpaidLeave;

    /// <summary>
    /// Absence hours inside the month, from approved requests only. Overlapping
    /// requests never pay twice — a day counts once however many requests cover it.
    /// </summary>
    internal static decimal HoursInMonth(
        IEnumerable<AbsenceRequest> requests, int year, int month, decimal standardHoursPerDay)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var holidays = IcelandicHolidays.GetHolidaysInMonth(year, month)
            .ToDictionary(h => h.Date, h => h.IsHalfDay);

        var countedDays = new Dictionary<DateOnly, decimal>();

        foreach (var request in requests)
        {
            if (request.Status != AbsenceStatus.Approved) continue;
            if (!CountsTowardDuty(request.Type)) continue;

            var from = request.StartDate > monthStart ? request.StartDate : monthStart;
            var to   = request.EndDate   < monthEnd   ? request.EndDate   : monthEnd;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var value = DayValue(day, holidays);
                if (value > 0) countedDays[day] = value;   // a day counts once
            }
        }

        return countedDays.Values.Sum() * standardHoursPerDay;
    }

    /// <summary>What one day is worth as duty: 1, a half-holiday 0.5, otherwise 0.</summary>
    private static decimal DayValue(DateOnly day, IReadOnlyDictionary<DateOnly, bool> holidays)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0m;
        if (holidays.TryGetValue(day, out var isHalfDay)) return isHalfDay ? 0.5m : 0m;
        return 1m;
    }
}
