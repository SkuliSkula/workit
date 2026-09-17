using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Who-created-this stamping. The API sets CreatedAt / CreatedByUserId /
/// CreatedByName on every record a user creates, resolving the name the same
/// way attachments always have: AppUser name, else Employee display name,
/// else email.
/// </summary>
public class CreatedAuditTests
{
    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid()}")
            .Options);

    private static HttpContext HttpContextWithEmail(string? email)
    {
        var claims = email is null ? [] : new[] { new Claim(ClaimTypes.Email, email) };
        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) };
    }

    [Fact]
    public async Task Stamp_UsesAppUserName_WhenPresent()
    {
        await using var db = NewDb();
        var owner = new AppUser { Email = "owner@x.is", Name = "Hulda", Role = WorkitRoles.Owner, CompanyId = Guid.NewGuid() };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var user = new UserContext { UserId = owner.Id, CompanyId = owner.CompanyId!.Value, Role = WorkitRoles.Owner };

        var job = new Job { Name = "x", CreatedByName = "client lies", CreatedByUserId = Guid.NewGuid() };
        var before = DateTimeOffset.UtcNow;
        await job.StampCreatedAsync(db, HttpContextWithEmail("owner@x.is"), user, CancellationToken.None);

        job.CreatedByUserId.Should().Be(owner.Id);
        job.CreatedByName.Should().Be("Hulda");
        job.CreatedAt.Should().NotBeNull().And.BeOnOrAfter(before);
    }

    [Fact]
    public async Task Stamp_FallsBackToEmployeeDisplayName_ForEmployeeLogins()
    {
        await using var db = NewDb();
        var companyId = Guid.NewGuid();
        var employee = new Employee { CompanyId = companyId, DisplayName = "Anna Demósdóttir", Email = "anna@x.is" };
        var login = new AppUser { Email = "anna@x.is", Role = WorkitRoles.Employee, CompanyId = companyId, EmployeeId = employee.Id };
        db.Employees.Add(employee);
        db.AppUsers.Add(login);
        await db.SaveChangesAsync();
        var user = new UserContext { UserId = login.Id, CompanyId = companyId, EmployeeId = employee.Id, Role = WorkitRoles.Employee };

        var entry = new TimeEntry { CompanyId = companyId, EmployeeId = employee.Id };
        await entry.StampCreatedAsync(db, HttpContextWithEmail("anna@x.is"), user, CancellationToken.None);

        entry.CreatedByName.Should().Be("Anna Demósdóttir");
        entry.CreatedByUserId.Should().Be(login.Id);
    }

    [Fact]
    public async Task Stamp_FallsBackToEmail_WhenNoNameIsKnown()
    {
        await using var db = NewDb();
        var user = new UserContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Role = WorkitRoles.Admin };

        var customer = new Customer { Name = "c" };
        await customer.StampCreatedAsync(db, HttpContextWithEmail("someone@x.is"), user, CancellationToken.None);

        customer.CreatedByName.Should().Be("someone@x.is");
    }

    [Fact]
    public void OwnerEmployee_IsStampedAsSelfCreated()
    {
        var owner = new AppUser { Email = "owner@x.is", Name = "Hulda", Role = WorkitRoles.Owner };

        var employee = OwnerEmployeeHelper.CreateEmployeeForOwner(owner, Guid.NewGuid());

        employee.CreatedByUserId.Should().Be(owner.Id);
        employee.CreatedByName.Should().Be("Hulda");
        employee.CreatedAt.Should().NotBeNull();
    }
}
