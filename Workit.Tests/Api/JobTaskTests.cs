using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Tasks inside jobs: who may see one, what a time entry may point at, what
/// deleting a task does to its hours, and the code format.
/// </summary>
public class JobTaskTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static UserContext Employee(Guid? employeeId = null) => new()
    {
        UserId = Guid.NewGuid(), CompanyId = CompanyId, EmployeeId = employeeId ?? Me, Role = WorkitRoles.Employee,
    };

    private static UserContext Owner() => new() { UserId = Guid.NewGuid(), CompanyId = CompanyId, Role = WorkitRoles.Owner };

    private static Job JobFor(params Guid[] assignees) => new() { CompanyId = CompanyId, Name = "j", AssignedEmployeeIds = [.. assignees] };
    private static JobTask TaskFor(Job job, params Guid[] assignees) => new() { CompanyId = CompanyId, JobId = job.Id, Name = "t", AssignedEmployeeIds = [.. assignees] };

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"tasks-{Guid.NewGuid()}").Options);

    // ── Visibility ────────────────────────────────────────────────────────────

    [Fact]
    public void Employee_SeesTaskNamingNobody_OnTheirJob()
    {
        var job = JobFor(Me);
        Employee().CanSeeTask(job, TaskFor(job)).Should().BeTrue();
    }

    [Fact]
    public void Employee_SeesTaskNamingThem()
    {
        var job = JobFor(Me, Colleague);
        Employee().CanSeeTask(job, TaskFor(job, Colleague, Me)).Should().BeTrue();
    }

    [Fact]
    public void Employee_DoesNotSeeTaskNamingOnlyOthers()
    {
        var job = JobFor(Me, Colleague);
        Employee().CanSeeTask(job, TaskFor(job, Colleague)).Should().BeFalse();
    }

    [Fact]
    public void Employee_DoesNotSeeAnyTask_OnAJobTheyAreNotOn()
    {
        var job = JobFor(Colleague);
        Employee().CanSeeTask(job, TaskFor(job)).Should().BeFalse();       // even an unassigned task
        Employee().CanSeeTask(job, TaskFor(job, Me)).Should().BeFalse();   // even one naming them — the job gates first
    }

    [Fact]
    public void Owner_SeesEveryTask()
    {
        var job = JobFor(Colleague);
        Owner().CanSeeTask(job, TaskFor(job, Colleague)).Should().BeTrue();
    }

    // ── Time entry → task ─────────────────────────────────────────────────────

    [Fact]
    public async Task Entry_MayNameNoTask()
    {
        await using var db = NewDb();
        (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, CompanyId, Guid.NewGuid(), null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Entry_MayNameATaskOnItsOwnJob_ButNotAnotherJobs()
    {
        await using var db = NewDb();
        var job = JobFor(Me); var other = JobFor(Me);
        var task = TaskFor(job);
        db.Jobs.AddRange(job, other); db.JobTasks.Add(task);
        await db.SaveChangesAsync();

        (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, CompanyId, job.Id, task.Id, CancellationToken.None)).Should().BeNull();
        (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, CompanyId, other.Id, task.Id, CancellationToken.None)).Should().Contain("different job");
        (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, CompanyId, job.Id, Guid.NewGuid(), CancellationToken.None)).Should().Contain("not found");
        (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, Guid.NewGuid(), job.Id, task.Id, CancellationToken.None)).Should().Contain("not found"); // other company
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingATask_KeepsItsHoursOnTheJob()
    {
        await using var db = NewDb();
        var job = JobFor(Me); var task = TaskFor(job);
        var e1 = new TimeEntry { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, TaskId = task.Id, Hours = 4 };
        var e2 = new TimeEntry { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, TaskId = task.Id, Hours = 3 };
        db.Jobs.Add(job); db.JobTasks.Add(task); db.TimeEntries.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var refused = await JobTaskEndpoints.DeleteTaskAsync(db, task, CancellationToken.None);

        refused.Should().BeNull();
        (await db.JobTasks.CountAsync()).Should().Be(0);
        var entries = await db.TimeEntries.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.TaskId == null && e.JobId == job.Id);
        entries.Sum(e => e.Hours).Should().Be(7);
    }

    [Fact]
    public async Task DeletingATask_IsRefusedWhenAnyHourIsInvoiced()
    {
        await using var db = NewDb();
        var job = JobFor(Me); var task = TaskFor(job);
        db.Jobs.Add(job); db.JobTasks.Add(task);
        db.TimeEntries.Add(new TimeEntry { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, TaskId = task.Id, Hours = 2, IsInvoiced = true });
        await db.SaveChangesAsync();

        var refused = await JobTaskEndpoints.DeleteTaskAsync(db, task, CancellationToken.None);

        refused.Should().Contain("invoice");
        (await db.JobTasks.CountAsync()).Should().Be(1);
        (await db.TimeEntries.SingleAsync()).TaskId.Should().Be(task.Id);
    }

    // ── Code ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("MNT102", 3, "MNT102-3")]
    [InlineData("REP107", 12, "REP107-12")]
    public void TaskCode_IsJobCodeDashNumber(string jobCode, int n, string expected)
    {
        JobTaskEndpoints.FormatTaskCode(jobCode, n).Should().Be(expected);
    }
}
