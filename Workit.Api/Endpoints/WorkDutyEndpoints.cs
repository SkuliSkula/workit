using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Utilities;

namespace Workit.Api.Endpoints;

internal static class WorkDutyEndpoints
{
    internal static void MapWorkDutyEndpoints(this WebApplication app)
    {
        // The apps need the calendar to count an absence period properly: a
        // four-week holiday is working days, and a public holiday inside it is
        // not one of them. Range rather than month, since a period spans months.
        app.MapGet("/api/holidays", (DateOnly from, DateOnly to) =>
        {
            if (to < from) return Results.BadRequest("The end date must not be before the start date.");
            if (to.DayNumber - from.DayNumber > 800) return Results.BadRequest("Ask for a shorter range.");

            var holidays = new List<HolidayInfo>();
            for (var year = from.Year; year <= to.Year; year++)
            {
                for (var month = 1; month <= 12; month++)
                {
                    foreach (var holiday in IcelandicHolidays.GetHolidaysInMonth(year, month))
                    {
                        if (holiday.Date < from || holiday.Date > to) continue;
                        holidays.Add(new HolidayInfo
                        {
                            Date = holiday.Date.ToString("yyyy-MM-dd"),
                            Name = holiday.Name,
                            IsHalfDay = holiday.IsHalfDay,
                        });
                    }
                }
            }
            return Results.Ok(holidays.OrderBy(h => h.Date).ToList());
        }).RequireAuthorization().WithTags("Work Duty").WithName("GetHolidays");

        app.MapGet("/api/workduty", async (
            int year,
            int month,
            WorkitDbContext db,
            HttpContext httpContext) =>
        {
            var userContext = httpContext.User.ToUserContext();

            // Get company for standard hours
            var company = await db.Companies.FindAsync(userContext.CompanyId);
            var standardHours = company?.StandardHoursPerDay ?? 8m;

            // Calculate work duty
            var holidays = IcelandicHolidays.GetHolidaysInMonth(year, month);
            var dutyHours = IcelandicHolidays.GetWorkDutyHours(year, month, standardHours);

            // Get hours worked for the authenticated employee
            var startDate = new DateOnly(year, month, 1);
            var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            // Resolve employee ID — use JWT claim first, fall back to email lookup for owners
            var resolvedEmployeeId = userContext.EmployeeId;
            if (resolvedEmployeeId is null)
            {
                var email = httpContext.User.FindFirstValue(ClaimTypes.Email)
                         ?? httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    var emp = await db.Employees
                        .FirstOrDefaultAsync(e => e.CompanyId == userContext.CompanyId
                                               && e.Email == email);
                    resolvedEmployeeId = emp?.Id;
                }
            }

            decimal hoursWorked = 0;
            decimal absenceHours = 0;
            if (resolvedEmployeeId is Guid empId)
            {
                // Per day, not just the total: absence fills what the day's work
                // left, so the two together never exceed the day's duty.
                var workedByDay = await db.TimeEntries
                    .Where(t => t.CompanyId == userContext.CompanyId
                             && t.EmployeeId == empId
                             && t.WorkDate >= startDate
                             && t.WorkDate <= endDate)
                    .GroupBy(t => t.WorkDate)
                    .Select(g => new { Day = g.Key, Hours = g.Sum(t => t.Hours) })
                    .ToDictionaryAsync(x => x.Day, x => x.Hours);

                hoursWorked = workedByDay.Values.Sum();

                // Approved absence covers the duty it overlaps — a sick day is a
                // day, not the hours someone might otherwise have logged.
                var absences = await db.AbsenceRequests
                    .Where(a => a.CompanyId == userContext.CompanyId
                             && a.EmployeeId == empId
                             && a.Status == AbsenceStatus.Approved
                             && a.StartDate <= endDate
                             && a.EndDate >= startDate)
                    .ToListAsync();
                absenceHours = AbsenceDuty.HoursInMonth(absences, year, month, standardHours, workedByDay);
            }

            var hoursCounted = hoursWorked + absenceHours;

            // Count weekdays, holidays
            var daysInMonth = DateTime.DaysInMonth(year, month);
            int weekdays = 0;
            for (int d = 1; d <= daysInMonth; d++)
            {
                var dow = new DateOnly(year, month, d).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) weekdays++;
            }

            var fullHolidays = holidays.Count(h => !h.IsHalfDay && h.Date.DayOfWeek != DayOfWeek.Saturday && h.Date.DayOfWeek != DayOfWeek.Sunday);
            var halfHolidays = holidays.Count(h => h.IsHalfDay && h.Date.DayOfWeek != DayOfWeek.Saturday && h.Date.DayOfWeek != DayOfWeek.Sunday);

            var remaining = Math.Max(0, dutyHours - hoursCounted);
            var pct = dutyHours > 0 ? Math.Round(hoursCounted / dutyHours * 100, 1) : 0;

            return Results.Ok(new WorkDutyResponse
            {
                Year = year,
                Month = month,
                TotalCalendarDays = daysInMonth,
                WeekdaysInMonth = weekdays,
                FullHolidays = fullHolidays,
                HalfHolidays = halfHolidays,
                WorkDutyHours = dutyHours,
                HoursWorked = hoursWorked,
                AbsenceHours = absenceHours,
                HoursCounted = hoursCounted,
                HoursRemaining = remaining,
                CompletionPercentage = pct,
                Holidays = holidays.Select(h => new HolidayInfo
                {
                    Date = h.Date.ToString("yyyy-MM-dd"),
                    Name = h.Name,
                    IsHalfDay = h.IsHalfDay
                }).ToList()
            });
        }).RequireAuthorization().WithTags("Work Duty");
    }
}
