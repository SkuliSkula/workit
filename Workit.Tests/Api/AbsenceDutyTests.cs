using FluentAssertions;
using Workit.Api.Endpoints;
using Workit.Shared.Models;
using Workit.Shared.Utilities;

namespace Workit.Tests.Api;

/// <summary>
/// Absence counts toward the month's duty, a standard day at a time. Two days
/// sick is two days — never the hours someone might otherwise have logged.
/// </summary>
public class AbsenceDutyTests
{
    private const decimal Standard = 8m;

    private static AbsenceRequest Absence(
        DateOnly from, DateOnly to,
        AbsenceType type = AbsenceType.SickLeave,
        AbsenceStatus status = AbsenceStatus.Approved) => new()
    {
        EmployeeId = Guid.NewGuid(), Type = type, Status = status, StartDate = from, EndDate = to,
    };

    private static decimal Hours(params AbsenceRequest[] requests) =>
        AbsenceDuty.HoursInMonth(requests, 2026, 9, Standard);

    [Fact]
    public void Two_sick_days_count_sixteen_hours()
    {
        // Mon 7 – Tue 8 September 2026, both ordinary weekdays.
        Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8))).Should().Be(16m);
    }

    [Fact]
    public void A_single_day_is_capped_at_the_standard_day()
    {
        Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7))).Should().Be(8m);
    }

    [Fact]
    public void A_weekend_inside_the_absence_adds_nothing()
    {
        // Fri 4 – Mon 7 September: Saturday and Sunday are not duty.
        Hours(Absence(new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 7))).Should().Be(16m);
    }

    [Fact]
    public void Overlapping_requests_never_pay_the_same_day_twice()
    {
        var a = Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 9));
        var b = Absence(new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 10), AbsenceType.Vacation);
        Hours(a, b).Should().Be(32m);   // 7, 8, 9, 10 — four weekdays, not seven days' worth
    }

    [Fact]
    public void Only_approved_absence_counts()
    {
        foreach (var status in new[] { AbsenceStatus.Pending, AbsenceStatus.Denied, AbsenceStatus.Cancelled })
        {
            Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8), status: status))
                .Should().Be(0m, "{0} absence is not settled", status);
        }
    }

    [Fact]
    public void Unpaid_leave_does_not_count_but_every_other_type_does()
    {
        Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), AbsenceType.UnpaidLeave))
            .Should().Be(0m);

        foreach (var type in Enum.GetValues<AbsenceType>().Where(t => t != AbsenceType.UnpaidLeave))
        {
            Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), type))
                .Should().Be(8m, "{0} covers the duty", type);
        }
    }

    [Fact]
    public void Absence_spilling_outside_the_month_only_counts_the_days_inside_it()
    {
        // 28 Aug – 2 Sep: only Tue 1 and Wed 2 September belong to this month.
        Hours(Absence(new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 2))).Should().Be(16m);
    }

    [Fact]
    public void A_month_of_absence_never_exceeds_the_month_of_duty()
    {
        var wholeMonth = Absence(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Hours(wholeMonth).Should().Be(IcelandicHolidays.GetWorkDutyHours(2026, 9, Standard));
    }

    [Fact]
    public void A_full_holiday_is_not_a_day_to_be_absent_from()
    {
        // Whichever month holds a full holiday, an absence over it counts only
        // the working days around it — proven against the duty calculation.
        var year = 2026;
        foreach (var month in Enumerable.Range(1, 12))
        {
            var last = DateTime.DaysInMonth(year, month);
            var all  = AbsenceDuty.HoursInMonth(
                [Absence(new DateOnly(year, month, 1), new DateOnly(year, month, last))], year, month, Standard);
            all.Should().Be(IcelandicHolidays.GetWorkDutyHours(year, month, Standard),
                "absence over a whole month equals that month's duty ({0}/{1})", month, year);
        }
    }
}
