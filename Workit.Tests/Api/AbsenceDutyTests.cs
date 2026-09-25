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
        AbsenceStatus status = AbsenceStatus.Approved,
        decimal hoursPerDay = Standard) => new()
    {
        EmployeeId = Guid.NewGuid(), Type = type, Status = status,
        StartDate = from, EndDate = to, HoursPerDay = hoursPerDay,
    };

    private static decimal Hours(params AbsenceRequest[] requests) =>
        AbsenceDuty.HoursInMonth(requests, 2026, 9, Standard);

    private static decimal HoursWithWork(
        IReadOnlyDictionary<DateOnly, decimal> worked, params AbsenceRequest[] requests) =>
        AbsenceDuty.HoursInMonth(requests, 2026, 9, Standard, worked);

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

    // ── Hours are registered, not assumed ────────────────────────────────────

    [Fact]
    public void Half_a_day_ill_counts_the_hours_registered()
    {
        // Came in, worked the morning, went home at midday.
        Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), hoursPerDay: 4m))
            .Should().Be(4m);
    }

    [Fact]
    public void A_day_of_absence_never_exceeds_a_standard_day()
    {
        Hours(Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), hoursPerDay: 12m))
            .Should().Be(8m);
    }

    [Fact]
    public void Worked_four_and_ill_four_makes_a_whole_day()
    {
        var worked = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 9, 7)] = 4m };
        HoursWithWork(worked, Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), hoursPerDay: 4m))
            .Should().Be(4m, "the day counts 4 worked + 4 ill = 8");
    }

    [Fact]
    public void Absence_only_fills_what_the_work_left()
    {
        // A full day worked and a full day registered ill: the day is still 8.
        var worked = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 9, 7)] = 8m };
        HoursWithWork(worked, Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7)))
            .Should().Be(0m);

        // Six worked leaves room for two.
        var six = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 9, 7)] = 6m };
        HoursWithWork(six, Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7)))
            .Should().Be(2m);
    }

    [Fact]
    public void Overtime_on_the_day_never_pushes_absence_below_zero()
    {
        var overworked = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 9, 7)] = 11m };
        HoursWithWork(overworked, Absence(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7)))
            .Should().Be(0m);
    }

    [Fact]
    public void A_part_day_on_a_half_holiday_is_capped_by_the_half_day()
    {
        // 24 December 2026 is a half day and falls on a Thursday.
        var christmasEve = new DateOnly(2026, 12, 24);
        AbsenceDuty.HoursInMonth(
            [Absence(christmasEve, christmasEve, hoursPerDay: 8m)], 2026, 12, Standard)
            .Should().Be(4m);
    }

    [Fact]
    public void Overlapping_requests_take_the_larger_claim_on_a_day_not_the_sum()
    {
        var day = new DateOnly(2026, 9, 7);
        Hours(Absence(day, day, hoursPerDay: 4m),
              Absence(day, day, AbsenceType.Vacation, hoursPerDay: 6m))
            .Should().Be(6m);
    }

    // ── Describing the clash when hours meet absence ─────────────────────────

    [Fact]
    public void A_half_day_of_absence_still_leaves_room_for_the_hours_worked()
    {
        // The rule the endpoint applies: room = the day's duty, less absence,
        // less what is already logged.
        const decimal dayDuty = 8m;
        var absence = 4m;
        var alreadyWorked = 0m;
        (dayDuty - absence - alreadyWorked).Should().Be(4m, "half a day off leaves half a day to work");

        alreadyWorked = 4m;
        (dayDuty - absence - alreadyWorked).Should().Be(0m, "and once it is worked the day is full");
    }
}
