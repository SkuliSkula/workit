using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Self-service on your own account: correcting your details, and deleting the
/// account (App Store guideline 5.1.1(v)). Employment terms belong to the
/// employer and must survive both.
/// </summary>
public class AccountEndpointsTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"account-{Guid.NewGuid()}").Options);
        db.Companies.Add(new Company { Id = CompanyId, Name = "ÓS rafverktakar ehf." });
        db.SaveChanges();
        return db;
    }

    private static (AppUser user, Employee employee) Seed(WorkitDbContext db, string role = WorkitRoles.Employee)
    {
        var employee = new Employee
        {
            CompanyId = CompanyId, DisplayName = "Anna Demósdóttir", Email = "anna@os.is",
            Phone = "555 1111", Ssn = "0101801234", Trade = "Rafvirki",
            EmploymentType = EmploymentType.Employed, HourlySalary = 4200, HourlyBillableRate = 12000,
            Address = "Gamla gata 1", ZipCode = "105", City = "Reykjavík",
        };
        db.Employees.Add(employee);
        var user = new AppUser
        {
            CompanyId = CompanyId, EmployeeId = employee.Id, Name = "Anna Demósdóttir",
            Email = "anna@os.is", Role = role, PasswordHash = "x",
        };
        db.AppUsers.Add(user);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, Token = "t", ExpiresUtc = DateTime.UtcNow.AddDays(7) });
        db.PasswordResetTokens.Add(new PasswordResetToken { Email = user.Email, Token = "r", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = CompanyId });
        db.SaveChanges();
        return (user, employee);
    }

    private static UserContext Context(AppUser user) =>
        new() { UserId = user.Id, CompanyId = CompanyId, EmployeeId = user.EmployeeId, Role = user.Role };

    // ── Editing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_changes_contact_details_and_leaves_employment_terms_alone()
    {
        await using var db = NewDb();
        var (user, employee) = Seed(db);

        await AccountEndpoints.UpdateAsync(db, Context(user),
            new MyAccountUpdate("Anna Jónsdóttir", " 555 9999 ", "Nýja gata 2", "200", "Kópavogur"), CancellationToken.None);

        var saved = await db.Employees.SingleAsync(e => e.Id == employee.Id);
        saved.DisplayName.Should().Be("Anna Jónsdóttir");
        saved.Phone.Should().Be("555 9999");           // trimmed
        saved.Address.Should().Be("Nýja gata 2");
        saved.City.Should().Be("Kópavogur");
        // The employer's business:
        saved.HourlySalary.Should().Be(4200);
        saved.HourlyBillableRate.Should().Be(12000);
        saved.Ssn.Should().Be("0101801234");
        saved.Trade.Should().Be("Rafvirki");
        saved.CompanyId.Should().Be(CompanyId);
        saved.EmploymentType.Should().Be(EmploymentType.Employed);
        // The sign-in email is the identity the invite was issued to.
        (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).Email.Should().Be("anna@os.is");
    }

    [Fact]
    public async Task Update_refuses_an_empty_name()
    {
        await using var db = NewDb();
        var (user, _) = Seed(db);
        var ok = await AccountEndpoints.UpdateAsync(db, Context(user), new MyAccountUpdate("   ", null, null, null, null), CancellationToken.None);
        ok.Should().BeFalse();
        (await db.Employees.SingleAsync()).DisplayName.Should().Be("Anna Demósdóttir");
    }

    // ── Deleting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_the_sign_in_but_keeps_the_hours_with_the_employer()
    {
        await using var db = NewDb();
        var (user, employee) = Seed(db);
        db.TimeEntries.Add(new TimeEntry
        {
            CompanyId = CompanyId, EmployeeId = employee.Id, JobId = Guid.NewGuid(),
            WorkDate = new DateOnly(2026, 9, 1), Hours = 8, Notes = "Lagnavinna",
        });
        await db.SaveChangesAsync();

        var deleted = await AccountEndpoints.DeleteAsync(db, Context(user), CancellationToken.None);

        deleted.Should().BeTrue();
        (await db.AppUsers.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
        (await db.RefreshTokens.AnyAsync(t => t.UserId == user.Id)).Should().BeFalse();
        (await db.PasswordResetTokens.AnyAsync(t => t.Email == "anna@os.is")).Should().BeFalse();
        (await db.UserCompanies.AnyAsync(c => c.UserId == user.Id)).Should().BeFalse();

        // Payroll records stay, attached to a now-inactive employee.
        var stillThere = await db.Employees.SingleAsync(e => e.Id == employee.Id);
        stillThere.IsActive.Should().BeFalse();
        var entry = await db.TimeEntries.SingleAsync();
        entry.EmployeeId.Should().Be(employee.Id);
        entry.Hours.Should().Be(8);
    }

    [Fact]
    public async Task The_last_owner_of_a_company_cannot_delete_their_account()
    {
        await using var db = NewDb();
        var (owner, _) = Seed(db, WorkitRoles.Owner);

        (await AccountEndpoints.CanDeleteAccountAsync(db, Context(owner), CancellationToken.None)).Should().BeFalse();
        (await AccountEndpoints.DeleteAsync(db, Context(owner), CancellationToken.None)).Should().BeFalse();
        (await db.AppUsers.AnyAsync(u => u.Id == owner.Id)).Should().BeTrue();

        // With a second owner in place it is allowed again.
        db.AppUsers.Add(new AppUser { CompanyId = CompanyId, Name = "Ólafur", Email = "olafur@os.is", Role = WorkitRoles.Owner, PasswordHash = "x" });
        await db.SaveChangesAsync();
        (await AccountEndpoints.CanDeleteAccountAsync(db, Context(owner), CancellationToken.None)).Should().BeTrue();
        (await AccountEndpoints.DeleteAsync(db, Context(owner), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task An_employee_can_always_delete()
    {
        await using var db = NewDb();
        var (user, _) = Seed(db);
        (await AccountEndpoints.CanDeleteAccountAsync(db, Context(user), CancellationToken.None)).Should().BeTrue();
    }
}
