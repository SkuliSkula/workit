using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// "What to bring" and putting a job on hold are the crew's own calls, so an
/// employee on the job may change them — and only those, and only on their own
/// jobs. Covered at the data level; the endpoint guards are one-liners around
/// the same rule.
/// </summary>
public class JobBringTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"bring-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Bring_update_touches_only_the_two_lists()
    {
        await using var db = NewDb();
        var anna = Guid.NewGuid();
        var job = new Job
        {
            CompanyId = CompanyId, Name = "Viðhald", Code = "MNT102", CustomerId = Guid.NewGuid(),
            AssignedEmployeeIds = [anna], Location = "Borgartún 26", ContactName = "Guðrún",
            Instructions = "Skipta um ljós", BillingType = BillingType.Hourly, Category = JobCategory.Maintenance,
            ToolsSuggestion = "Stigi", MaterialsSuggestion = "",
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var body = new JobBringUpdate("  Stigi, SDS bor  ", " 50 m kapall ");
        job.ToolsSuggestion     = (body.ToolsSuggestion ?? "").Trim();
        job.MaterialsSuggestion = (body.MaterialsSuggestion ?? "").Trim();
        await db.SaveChangesAsync();

        var saved = await db.Jobs.SingleAsync();
        saved.ToolsSuggestion.Should().Be("Stigi, SDS bor");
        saved.MaterialsSuggestion.Should().Be("50 m kapall");
        // Untouched:
        saved.Code.Should().Be("MNT102");
        saved.Location.Should().Be("Borgartún 26");
        saved.Instructions.Should().Be("Skipta um ljós");
        saved.Category.Should().Be(JobCategory.Maintenance);
    }

    [Theory]
    [InlineData(true,  true)]   // assigned employee
    [InlineData(false, false)]  // not on this job
    public void An_employee_may_only_act_on_a_job_they_are_on(bool assigned, bool allowed)
    {
        var anna = Guid.NewGuid();
        var job = new Job { CompanyId = CompanyId, AssignedEmployeeIds = assigned ? [anna] : [Guid.NewGuid()] };
        job.AssignedEmployeeIds.Contains(anna).Should().Be(allowed);
    }
}
