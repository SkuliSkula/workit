using FluentAssertions;
using Workit.Shared.Utilities;

namespace Workit.Tests.Utilities;

public class IcelandicHolidaysTests
{
    [Fact]
    public void GetHolidays_Returns17Holidays_ForAnyYear()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        holidays.Should().HaveCount(17);
    }

    [Fact]
    public void GetHolidays_ReturnsSortedByDate()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        holidays.Should().BeInAscendingOrder(h => h.Date);
    }

    [Fact]
    public void GetHolidays_NewYearsDay_IsJanuary1()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        holidays.Should().Contain(h => h.Date == new DateOnly(2026, 1, 1) && h.Name == "New Year's Day" && !h.IsHalfDay);
    }

    [Fact]
    public void GetHolidays_NationalDay_IsJune17()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        holidays.Should().Contain(h => h.Date == new DateOnly(2026, 6, 17) && h.Name == "National Day");
    }

    [Fact]
    public void GetHolidays_ChristmasEve_IsHalfDay()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var christmasEve = holidays.Single(h => h.Name == "Christmas Eve");
        christmasEve.IsHalfDay.Should().BeTrue();
        christmasEve.Date.Should().Be(new DateOnly(2026, 12, 24));
    }

    [Fact]
    public void GetHolidays_NewYearsEve_IsHalfDay()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var newYearsEve = holidays.Single(h => h.Name == "New Year's Eve");
        newYearsEve.IsHalfDay.Should().BeTrue();
    }

    [Fact]
    public void GetHolidays_Epiphany_IsHalfDay()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var epiphany = holidays.Single(h => h.Name == "Epiphany");
        epiphany.IsHalfDay.Should().BeTrue();
    }

    [Theory]
    [InlineData(2024, 3, 31)] // Easter 2024
    [InlineData(2025, 4, 20)] // Easter 2025
    [InlineData(2026, 4, 5)]  // Easter 2026
    [InlineData(2027, 3, 28)] // Easter 2027
    public void GetHolidays_EasterSunday_IsCorrect(int year, int month, int day)
    {
        var holidays = IcelandicHolidays.GetHolidays(year);
        var easter = holidays.Single(h => h.Name == "Easter Sunday");
        easter.Date.Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void GetHolidays_MaundyThursday_Is3DaysBeforeEaster()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var easter = holidays.Single(h => h.Name == "Easter Sunday").Date;
        var maundyThursday = holidays.Single(h => h.Name == "Maundy Thursday").Date;
        maundyThursday.Should().Be(easter.AddDays(-3));
    }

    [Fact]
    public void GetHolidays_GoodFriday_Is2DaysBeforeEaster()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var easter = holidays.Single(h => h.Name == "Easter Sunday").Date;
        var goodFriday = holidays.Single(h => h.Name == "Good Friday").Date;
        goodFriday.Should().Be(easter.AddDays(-2));
    }

    [Fact]
    public void GetHolidays_AscensionDay_Is39DaysAfterEaster()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var easter = holidays.Single(h => h.Name == "Easter Sunday").Date;
        var ascension = holidays.Single(h => h.Name == "Ascension Day").Date;
        ascension.Should().Be(easter.AddDays(39));
    }

    [Fact]
    public void GetHolidays_WhitSunday_Is49DaysAfterEaster()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        var easter = holidays.Single(h => h.Name == "Easter Sunday").Date;
        var whitSunday = holidays.Single(h => h.Name == "Whit Sunday").Date;
        whitSunday.Should().Be(easter.AddDays(49));
    }

    [Theory]
    [InlineData(2024, 8, 5)]  // Aug 1 2024 is Thursday → first Monday is Aug 5
    [InlineData(2025, 8, 4)]  // Aug 1 2025 is Friday → first Monday is Aug 4
    [InlineData(2026, 8, 3)]  // Aug 1 2026 is Saturday → first Monday is Aug 3
    public void GetHolidays_CommerceDay_IsFirstMondayOfAugust(int year, int expectedMonth, int expectedDay)
    {
        var holidays = IcelandicHolidays.GetHolidays(year);
        var commerceDay = holidays.Single(h => h.Name == "Commerce Day");
        commerceDay.Date.Should().Be(new DateOnly(year, expectedMonth, expectedDay));
        commerceDay.Date.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Theory]
    [InlineData(2024, 4, 25)] // April 19 2024 is Friday → first Thursday is April 25
    [InlineData(2025, 4, 24)] // April 19 2025 is Saturday → first Thursday is April 24
    [InlineData(2026, 4, 23)] // April 19 2026 is Sunday → first Thursday is April 23
    public void GetHolidays_FirstDayOfSummer_IsFirstThursdayOnOrAfterApril19(int year, int expectedMonth, int expectedDay)
    {
        var holidays = IcelandicHolidays.GetHolidays(year);
        var summer = holidays.Single(h => h.Name == "First Day of Summer");
        summer.Date.Should().Be(new DateOnly(year, expectedMonth, expectedDay));
        summer.Date.DayOfWeek.Should().Be(DayOfWeek.Thursday);
    }

    [Fact]
    public void GetHolidays_AllHolidaysHaveIcelandicNames()
    {
        var holidays = IcelandicHolidays.GetHolidays(2026);
        holidays.Should().AllSatisfy(h => h.NameIcelandic.Should().NotBeNullOrWhiteSpace());
    }

    // --- GetHolidaysInMonth ---

    [Fact]
    public void GetHolidaysInMonth_January_Returns2Holidays()
    {
        // New Year's Day (Jan 1) + Epiphany (Jan 6)
        var holidays = IcelandicHolidays.GetHolidaysInMonth(2026, 1);
        holidays.Should().HaveCount(2);
        holidays.Should().Contain(h => h.Name == "New Year's Day");
        holidays.Should().Contain(h => h.Name == "Epiphany");
    }

    [Fact]
    public void GetHolidaysInMonth_December_Returns3Holidays()
    {
        // Christmas Eve (24), Christmas Day (25), 2nd Day (26), New Year's Eve (31)
        var holidays = IcelandicHolidays.GetHolidaysInMonth(2026, 12);
        holidays.Should().HaveCount(4);
    }

    [Fact]
    public void GetHolidaysInMonth_MonthWithNoHolidays_ReturnsEmpty()
    {
        // February 2026 has no fixed holidays; Easter-based ones won't land there for typical years
        var holidays = IcelandicHolidays.GetHolidaysInMonth(2026, 2);
        holidays.Should().BeEmpty();
    }

    // --- GetWorkingDaysInMonth ---

    [Fact]
    public void GetWorkingDaysInMonth_January2026_SubtractsHolidays()
    {
        // January 2026: 31 days, weekdays = 22 (Jan 1 = Thursday)
        // Jan 1 (New Year's, full holiday, Thursday) → -1
        // Jan 6 (Epiphany, half day, Tuesday) → -0.5
        // Expected: 22 - 1 - 0.5 = 20.5
        var workingDays = IcelandicHolidays.GetWorkingDaysInMonth(2026, 1);
        workingDays.Should().Be(20.5m);
    }

    [Fact]
    public void GetWorkingDaysInMonth_WeekendHolidays_AreNotDoubleSubtracted()
    {
        // If a holiday falls on a weekend, it shouldn't reduce working days
        // We need to verify the logic works - find a year where a holiday falls on weekend
        // 2022: Jan 1 is Saturday → should not reduce weekday count
        var workingDays2022 = IcelandicHolidays.GetWorkingDaysInMonth(2022, 1);
        // Jan 2022: 21 weekdays. Jan 1 Sat (no effect), Jan 6 Thu (half day, -0.5)
        workingDays2022.Should().Be(20.5m);
    }

    [Fact]
    public void GetWorkingDaysInMonth_MonthWithNoHolidays_ReturnsAllWeekdays()
    {
        // February 2026: 28 days, starts Sunday
        // Weekdays: Mon-Fri count = 20
        var workingDays = IcelandicHolidays.GetWorkingDaysInMonth(2026, 2);
        workingDays.Should().Be(20m);
    }

    // --- GetWorkDutyHours ---

    [Fact]
    public void GetWorkDutyHours_DefaultHoursPerDay_Is8()
    {
        var workingDays = IcelandicHolidays.GetWorkingDaysInMonth(2026, 2);
        var hours = IcelandicHolidays.GetWorkDutyHours(2026, 2);
        hours.Should().Be(workingDays * 8m);
    }

    [Fact]
    public void GetWorkDutyHours_CustomHoursPerDay()
    {
        var workingDays = IcelandicHolidays.GetWorkingDaysInMonth(2026, 2);
        var hours = IcelandicHolidays.GetWorkDutyHours(2026, 2, 7.5m);
        hours.Should().Be(workingDays * 7.5m);
    }
}
