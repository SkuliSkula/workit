using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Deleting a job: refused while anything with money on it points at the job,
/// otherwise the job goes together with its tasks and files and linked
/// expenses are set loose.
/// </summary>
public class JobDeleteTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();

    private static Job NewJob() => new() { CompanyId = CompanyId, Name = "j", Code = "REP101" };

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"jobdelete-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task DeletingAJob_TakesItsTasksAndFiles_AndUnlinksExpenses()
    {
        await using var db = NewDb();
        var job = NewJob(); var other = NewJob();
        var expense = new Expense { CompanyId = CompanyId, JobId = job.Id, JobLinkSource = ExpenseJobLinkSource.Manual };
        var otherExpense = new Expense { CompanyId = CompanyId, JobId = other.Id, JobLinkSource = ExpenseJobLinkSource.Automatic };
        db.Jobs.AddRange(job, other);
        db.JobTasks.AddRange(
            new JobTask { CompanyId = CompanyId, JobId = job.Id, Name = "t1" },
            new JobTask { CompanyId = CompanyId, JobId = job.Id, Name = "t2" },
            new JobTask { CompanyId = CompanyId, JobId = other.Id, Name = "keep" });
        db.JobAttachments.AddRange(
            new JobAttachment { CompanyId = CompanyId, JobId = job.Id, StorageKey = "a/1" },
            new JobAttachment { CompanyId = CompanyId, JobId = other.Id, StorageKey = "b/1" });
        db.Expenses.AddRange(expense, otherExpense);
        await db.SaveChangesAsync();

        var outcome = await JobEndpoints.DeleteJobAsync(db, job, CancellationToken.None);

        outcome.Refusal.Should().BeNull();
        outcome.StorageKeys.Should().Equal("a/1");
        (await db.Jobs.Select(j => j.Id).ToListAsync()).Should().Equal(other.Id);
        (await db.JobTasks.Select(t => t.Name).ToListAsync()).Should().Equal("keep");
        (await db.JobAttachments.Select(a => a.StorageKey).ToListAsync()).Should().Equal("b/1");

        var unlinked = await db.Expenses.SingleAsync(e => e.Id == expense.Id);
        unlinked.JobId.Should().BeNull();
        unlinked.JobLinkSource.Should().Be(ExpenseJobLinkSource.None);
        (await db.Expenses.SingleAsync(e => e.Id == otherExpense.Id)).JobId.Should().Be(other.Id);
    }

    [Fact]
    public async Task DeletingAJob_IsRefusedWhileItHasTimeEntries()
    {
        await using var db = NewDb();
        var job = NewJob();
        db.Jobs.Add(job);
        db.TimeEntries.AddRange(
            new TimeEntry { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, Hours = 2 },
            new TimeEntry { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, Hours = 3 });
        await db.SaveChangesAsync();

        var outcome = await JobEndpoints.DeleteJobAsync(db, job, CancellationToken.None);

        outcome.Refusal.Should().Contain("2 time entries");
        (await db.Jobs.CountAsync()).Should().Be(1);
        (await db.TimeEntries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DeletingAJob_IsRefusedWhileItHasMaterialEntries()
    {
        await using var db = NewDb();
        var job = NewJob();
        db.Jobs.Add(job);
        db.MaterialUsages.Add(new MaterialUsage { CompanyId = CompanyId, JobId = job.Id, EmployeeId = Me, MaterialId = Guid.NewGuid(), Quantity = 1 });
        await db.SaveChangesAsync();

        var outcome = await JobEndpoints.DeleteJobAsync(db, job, CancellationToken.None);

        outcome.Refusal.Should().Contain("material entry");
        (await db.Jobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeletingAJob_IsRefusedWhenExpensesWereBilledToIt()
    {
        await using var db = NewDb();
        var job = NewJob();
        db.Jobs.Add(job);
        db.ExpenseLineBillings.Add(new ExpenseLineBilling { CompanyId = CompanyId, ExpenseLineId = Guid.NewGuid(), JobId = job.Id, Quantity = 1 });
        await db.SaveChangesAsync();

        var outcome = await JobEndpoints.DeleteJobAsync(db, job, CancellationToken.None);

        outcome.Refusal.Should().Contain("billed");
        (await db.Jobs.CountAsync()).Should().Be(1);
    }
}
