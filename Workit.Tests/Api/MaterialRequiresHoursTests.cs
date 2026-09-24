using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Material is fitted during hours, so the hours come first: a usage only
/// stands where the same person already has time on that job that day.
/// </summary>
public class MaterialRequiresHoursTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Anna      = Guid.NewGuid();
    private static readonly Guid Baldur    = Guid.NewGuid();
    private static readonly Guid JobId     = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static WorkitDbContext NewDb()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"mat-hours-{Guid.NewGuid()}").Options);
        db.TimeEntries.Add(new TimeEntry
        {
            CompanyId = CompanyId, EmployeeId = Anna, JobId = JobId,
            WorkDate = Monday, Hours = 8, Notes = "Lagnavinna",
        });
        db.SaveChanges();
        return db;
    }

    /// <summary>The endpoint's rule, in isolation.</summary>
    private static Task<bool> HasHoursAsync(WorkitDbContext db, Guid employeeId, Guid jobId, DateOnly day) =>
        db.TimeEntries.AnyAsync(e => e.CompanyId == CompanyId && e.EmployeeId == employeeId
                                  && e.JobId == jobId && e.WorkDate == day);

    [Fact]
    public async Task The_person_who_worked_that_day_may_log_material()
    {
        await using var db = NewDb();
        (await HasHoursAsync(db, Anna, JobId, Monday)).Should().BeTrue();
    }

    [Fact]
    public async Task A_different_day_has_no_hours_behind_it()
    {
        await using var db = NewDb();
        (await HasHoursAsync(db, Anna, JobId, Monday.AddDays(1))).Should().BeFalse();
    }

    [Fact]
    public async Task Someone_else_was_not_on_that_job_that_day()
    {
        await using var db = NewDb();
        (await HasHoursAsync(db, Baldur, JobId, Monday)).Should().BeFalse();
    }

    [Fact]
    public async Task Hours_on_another_job_do_not_count()
    {
        await using var db = NewDb();
        (await HasHoursAsync(db, Anna, Guid.NewGuid(), Monday)).Should().BeFalse();
    }

}
