using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// The platform admin overview: per-company counts, the recent window, and
/// last activity across the different kinds of record.
/// </summary>
public class AdminOverviewTests
{
    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"overview-{Guid.NewGuid()}")
            .Options);

    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Overview_CountsPerCompany_AndSplitsRecentFromLifetime()
    {
        await using var db = NewDb();
        var busy  = new Company { Name = "Busy ehf." };
        var quiet = new Company { Name = "Quiet ehf." };
        var empty = new Company { Name = "Empty ehf." };
        db.Companies.AddRange(busy, quiet, empty);

        var owner = new AppUser { Email = "o@busy.is", Role = WorkitRoles.Owner, CompanyId = busy.Id };
        var pending = new AppUser { Email = "new@owner.is", Role = WorkitRoles.Owner, CompanyId = null };
        db.AppUsers.AddRange(owner, pending);
        db.UserCompanies.Add(new UserCompany { UserId = owner.Id, CompanyId = busy.Id, CreatedUtc = new DateTime(2026, 1, 5) });

        var e1 = new Employee { CompanyId = busy.Id, DisplayName = "A" };
        var e2 = new Employee { CompanyId = busy.Id, DisplayName = "B", IsActive = false };
        var e3 = new Employee { CompanyId = quiet.Id, DisplayName = "C" };
        db.Employees.AddRange(e1, e2, e3);

        var job = new Job { CompanyId = busy.Id, Name = "j", CreatedAt = Now.AddDays(-3) };
        var oldJob = new Job { CompanyId = busy.Id, Name = "old", CreatedAt = Now.AddDays(-90) };
        var quietJob = new Job { CompanyId = quiet.Id, Name = "q" };
        db.Jobs.AddRange(job, oldJob, quietJob);

        db.TimeEntries.AddRange(
            new TimeEntry { CompanyId = busy.Id, JobId = job.Id, EmployeeId = e1.Id, WorkDate = new DateOnly(2026, 9, 16), Hours = 8, OvertimeHours = 2 },
            new TimeEntry { CompanyId = busy.Id, JobId = job.Id, EmployeeId = e1.Id, WorkDate = new DateOnly(2026, 6, 1),  Hours = 5 },
            new TimeEntry { CompanyId = quiet.Id, JobId = quietJob.Id, EmployeeId = e3.Id, WorkDate = new DateOnly(2026, 7, 20), Hours = 4 });

        db.JobAttachments.Add(new JobAttachment { CompanyId = busy.Id, JobId = job.Id, UploadedAt = Now.AddDays(-1) });
        db.RefreshTokens.Add(new RefreshToken { UserId = owner.Id, CreatedUtc = Now.UtcDateTime.AddDays(-2), ExpiresUtc = Now.UtcDateTime.AddDays(28) });
        await db.SaveChangesAsync();

        var overview = await AdminOverviewEndpoints.BuildAsync(db, Now, CancellationToken.None);

        overview.RecentDays.Should().Be(30);
        var t = overview.Totals;
        t.Companies.Should().Be(3);
        t.CompaniesActiveRecent.Should().Be(1);       // only Busy did anything in the window
        t.Owners.Should().Be(2);
        t.OwnersWithoutCompany.Should().Be(1);
        t.Employees.Should().Be(3);
        t.EmployeesActive.Should().Be(2);
        t.Jobs.Should().Be(3);
        t.JobsRecent.Should().Be(1);
        t.Hours.Should().Be(19);                      // 8+2, 5, 4
        t.HoursRecent.Should().Be(10);
        t.TimeEntries.Should().Be(3);
        t.TimeEntriesRecent.Should().Be(1);
        t.Attachments.Should().Be(1);
        t.LoginsRecent.Should().Be(1);

        var rows = overview.Companies.ToDictionary(c => c.Name);
        rows["Busy ehf."].EmployeesActive.Should().Be(1);
        rows["Busy ehf."].Employees.Should().Be(2);
        rows["Busy ehf."].OwnerEmails.Should().Equal("o@busy.is");
        rows["Busy ehf."].Since.Should().Be(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        rows["Busy ehf."].LastActivity.Should().Be(Now.AddDays(-1));   // the upload beats the 16 Sep entry
        rows["Busy ehf."].LastLogin.Should().Be(Now.AddDays(-2));

        rows["Quiet ehf."].Hours.Should().Be(4);
        rows["Quiet ehf."].HoursRecent.Should().Be(0);
        rows["Quiet ehf."].LastActivity.Should().Be(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        rows["Quiet ehf."].OwnerEmails.Should().BeEmpty();

        rows["Empty ehf."].LastActivity.Should().BeNull();
        rows["Empty ehf."].Jobs.Should().Be(0);
    }

    [Fact]
    public async Task Overview_FlagsTheDemoCompany()
    {
        await using var db = NewDb();
        db.Companies.AddRange(new Company { Name = DemoDataSeeder.CompanyName }, new Company { Name = "Real ehf." });
        await db.SaveChangesAsync();

        var overview = await AdminOverviewEndpoints.BuildAsync(db, Now, CancellationToken.None);

        overview.Companies.Single(c => c.IsDemo).Name.Should().Be(DemoDataSeeder.CompanyName);
        overview.Companies.Single(c => !c.IsDemo).Name.Should().Be("Real ehf.");
    }
}
