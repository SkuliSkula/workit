using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// A job is finished once everything on it is billed — every hour and every
/// material. It is derived from the entries, so it cannot drift; there is no
/// reopen, and more work means a new job.
/// </summary>
public class JobClosureTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Other     = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"closure-{Guid.NewGuid()}").Options);

    private static Guid AddJob(WorkitDbContext db, string code, Guid? company = null)
    {
        var job = new Job { CompanyId = company ?? CompanyId, Name = code, Code = code };
        db.Jobs.Add(job);
        return job.Id;
    }

    private static void AddEntry(WorkitDbContext db, Guid jobId, bool invoiced, Guid? company = null) =>
        db.TimeEntries.Add(new TimeEntry
        {
            CompanyId = company ?? CompanyId, JobId = jobId, EmployeeId = Guid.NewGuid(),
            WorkDate = new DateOnly(2026, 9, 1), Hours = 8, IsInvoiced = invoiced,
        });

    private static void AddUsage(WorkitDbContext db, Guid jobId, bool invoiced) =>
        db.MaterialUsages.Add(new MaterialUsage
        {
            CompanyId = CompanyId, JobId = jobId, MaterialId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(), Quantity = 5, IsInvoiced = invoiced,
        });

    [Fact]
    public async Task A_job_is_finished_only_when_every_hour_and_material_is_invoiced()
    {
        await using var db = NewDb();
        var allBilled   = AddJob(db, "MNT101");
        var openHours   = AddJob(db, "MNT102");
        var openMateria = AddJob(db, "MNT103");
        var noHours     = AddJob(db, "MNT104");   // nothing logged yet
        var hoursOnly   = AddJob(db, "MNT105");   // billed hours, no materials

        AddEntry(db, allBilled, invoiced: true);
        AddEntry(db, allBilled, invoiced: true);
        AddUsage(db, allBilled, invoiced: true);

        AddEntry(db, openHours, invoiced: true);
        AddEntry(db, openHours, invoiced: false);
        AddUsage(db, openHours, invoiced: true);

        AddEntry(db, openMateria, invoiced: true);
        AddUsage(db, openMateria, invoiced: false);   // the material is still unbilled

        AddEntry(db, hoursOnly, invoiced: true);
        await db.SaveChangesAsync();

        var finished = await JobClosure.FinishedJobIdsAsync(db, CompanyId, CancellationToken.None);

        finished.Should().BeEquivalentTo([allBilled, hoursOnly]);
        finished.Should().NotContain(openHours);
        finished.Should().NotContain(openMateria);
        finished.Should().NotContain(noHours);

        // The single-job check agrees with the batch one.
        foreach (var (jobId, expected) in new[]
                 {
                     (allBilled, true), (openHours, false), (openMateria, false),
                     (noHours, false), (hoursOnly, true),
                 })
        {
            (await JobClosure.IsFinishedAsync(db, CompanyId, jobId, CancellationToken.None))
                .Should().Be(expected, "job {0}", jobId);
        }
    }

    [Fact]
    public async Task Another_company_cannot_finish_your_job()
    {
        await using var db = NewDb();
        var mine = AddJob(db, "MNT101");
        AddEntry(db, mine, invoiced: false);
        // An invoiced entry in another company on the same job id must not count.
        AddEntry(db, mine, invoiced: true, company: Other);
        await db.SaveChangesAsync();

        (await JobClosure.IsFinishedAsync(db, CompanyId, mine, CancellationToken.None)).Should().BeFalse();
        (await JobClosure.FinishedJobIdsAsync(db, CompanyId, CancellationToken.None)).Should().NotContain(mine);
    }

    [Fact]
    public async Task Materials_logged_without_a_job_never_hold_a_job_open()
    {
        await using var db = NewDb();
        var job = AddJob(db, "MNT101");
        AddEntry(db, job, invoiced: true);
        db.MaterialUsages.Add(new MaterialUsage
        {
            CompanyId = CompanyId, JobId = null, MaterialId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(), Quantity = 1, IsInvoiced = false,
        });
        await db.SaveChangesAsync();

        (await JobClosure.IsFinishedAsync(db, CompanyId, job, CancellationToken.None)).Should().BeTrue();
    }
}
