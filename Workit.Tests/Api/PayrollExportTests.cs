using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Hours → Payday timesheet: totals per employee for the month, SSN handling,
/// item names from the company, and what is left out.
/// </summary>
public class PayrollExportTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"payroll-{Guid.NewGuid()}").Options);

    private static async Task<(Employee anna, Employee noSsn)> SeedAsync(WorkitDbContext db, string drivingItem = "")
    {
        db.Companies.Add(new Company { Id = CompanyId, Name = "Test", PayrollRegularItemName = "Dagvinna", PayrollOvertimeItemName = "Yfirvinna", PayrollDrivingItemName = drivingItem });
        var anna  = new Employee { CompanyId = CompanyId, DisplayName = "Anna", Ssn = "010190-2989" };
        var noSsn = new Employee { CompanyId = CompanyId, DisplayName = "Baldur", Ssn = "" };
        db.Employees.AddRange(anna, noSsn);
        var job = Guid.NewGuid();
        db.TimeEntries.AddRange(
            new TimeEntry { CompanyId = CompanyId, JobId = job, EmployeeId = anna.Id,  WorkDate = new DateOnly(2026, 8, 3),  Hours = 8, OvertimeHours = 1, DrivingUnits = 2 },
            new TimeEntry { CompanyId = CompanyId, JobId = job, EmployeeId = anna.Id,  WorkDate = new DateOnly(2026, 8, 31), Hours = 7.5m, OvertimeHours = 0, DrivingUnits = 3 },
            new TimeEntry { CompanyId = CompanyId, JobId = job, EmployeeId = anna.Id,  WorkDate = new DateOnly(2026, 9, 1),  Hours = 8 },   // next month, excluded
            new TimeEntry { CompanyId = CompanyId, JobId = job, EmployeeId = noSsn.Id, WorkDate = new DateOnly(2026, 8, 5),  Hours = 6 });
        await db.SaveChangesAsync();
        return (anna, noSsn);
    }

    [Fact]
    public async Task Preview_TotalsTheMonthPerEmployee_AndFlagsMissingSsn()
    {
        await using var db = NewDb();
        var (anna, noSsn) = await SeedAsync(db);

        var preview = await PayrollEndpoints.BuildPreviewAsync(db, CompanyId, 2026, 8, CancellationToken.None);

        preview.RegularItemName.Should().Be("Dagvinna");
        preview.Rows.Should().HaveCount(2);
        var a = preview.Rows.Single(r => r.EmployeeId == anna.Id);
        a.RegularHours.Should().Be(15.5m); a.OvertimeHours.Should().Be(1); a.DrivingUnits.Should().Be(5); a.EntryCount.Should().Be(2);
        a.Ssn.Should().Be("0101902989"); a.HasSsn.Should().BeTrue();
        preview.Rows.Single(r => r.EmployeeId == noSsn.Id).HasSsn.Should().BeFalse();
        preview.LastExport.Should().BeNull();
    }

    [Fact]
    public async Task Timesheet_SkipsEmployeesWithoutSsn_ZeroItems_AndDrivingUnlessNamed()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var preview = await PayrollEndpoints.BuildPreviewAsync(db, CompanyId, 2026, 8, CancellationToken.None);

        var entries = PayrollEndpoints.ToTimesheet(preview);

        entries.Should().HaveCount(1);                        // Baldur has no SSN
        var e = entries[0];
        e.Ssn.Should().Be("0101902989"); e.Name.Should().Be("Anna");
        e.Items.Select(i => (i.Name, i.Quantity)).Should().Equal(("Dagvinna", 15.5), ("Yfirvinna", 1.0));   // no driving item named → not sent
    }

    [Fact]
    public async Task Timesheet_SendsDriving_WhenTheCompanyNamedAnItem()
    {
        await using var db = NewDb();
        await SeedAsync(db, drivingItem: "Akstur");
        var preview = await PayrollEndpoints.BuildPreviewAsync(db, CompanyId, 2026, 8, CancellationToken.None);

        var entries = PayrollEndpoints.ToTimesheet(preview);

        entries.Single().Items.Should().Contain(i => i.Name == "Akstur" && i.Quantity == 5);
    }

    [Fact]
    public async Task Preview_ReportsTheLastExportOfThatPeriodOnly()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        db.PayrollExports.AddRange(
            new PayrollExport { CompanyId = CompanyId, Year = 2026, Month = 8, SentAt = DateTimeOffset.UtcNow.AddDays(-2), EmployeesSent = 1, EmployeesRead = 0, SsnsNotOnRecord = "0101902989", PayloadJson = "[]" },
            new PayrollExport { CompanyId = CompanyId, Year = 2026, Month = 8, SentAt = DateTimeOffset.UtcNow.AddDays(-1), EmployeesSent = 1, EmployeesRead = 1, PayloadJson = "[]" },
            new PayrollExport { CompanyId = CompanyId, Year = 2026, Month = 7, SentAt = DateTimeOffset.UtcNow, EmployeesSent = 3, EmployeesRead = 3, PayloadJson = "[]" });
        await db.SaveChangesAsync();

        var preview = await PayrollEndpoints.BuildPreviewAsync(db, CompanyId, 2026, 8, CancellationToken.None);

        preview.LastExport!.EmployeesRead.Should().Be(1);
        preview.LastExport.PayloadJson.Should().BeEmpty();   // not shipped to the page
    }

    [Theory]
    [InlineData("010190-2989", "0101902989")]
    [InlineData(" 0101902989 ", "0101902989")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Ssn_IsReducedToDigits(string? input, string expected) =>
        PayrollEndpoints.NormalizeSsn(input).Should().Be(expected);
}
