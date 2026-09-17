using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class EmployeeEndpoints
{
    internal static void MapEmployeeEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Employees");
        var logger = app.Logger;

        securedApi.MapGet("/employees", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    IQueryable<Employee> query = db.Employees.Where(x => x.CompanyId == userContext.CompanyId);
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        if (userContext.EmployeeId is not Guid currentEmployeeId)
                        {
                            return Results.Forbid();
                        }

                        query = query.Where(x => x.Id == currentEmployeeId);
                    }
                    else if (!string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                             && !string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                    {
                        return Results.Forbid();
                    }

                    var employees = await query.OrderBy(x => x.DisplayName).ToListAsync(ct);
                    return Results.Ok(employees);
                },
                logger,
                "loading employees"))
            .WithName("GetEmployees");

        securedApi.MapPost("/employees", async (
                WorkitDbContext db,
                HttpContext httpContext,
                IAccountInviteService invites,
                CreateEmployeeUserRequest request,
                CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (!IsValidEmployee(request.Employee))
                    {
                        return Results.BadRequest("Employee name, SSN, and email are required.");
                    }

                    var normalizedEmail = request.Employee.Email.Trim().ToLowerInvariant();
                    if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail, ct))
                    {
                        return Results.Conflict("That email address is already in use.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var employee = new Employee
                    {
                        CompanyId = userContext.CompanyId,
                        DisplayName = request.Employee.DisplayName.Trim(),
                        Trade = request.Employee.Trade.Trim(),
                        Ssn = request.Employee.Ssn.Trim(),
                        Email = normalizedEmail,
                        Phone = request.Employee.Phone.Trim(),
                        ContactPerson = request.Employee.ContactPerson.Trim(),
                        EmploymentType = request.Employee.EmploymentType,
                        HourlySalary = request.Employee.HourlySalary,
                        HourlyBillableRate = request.Employee.HourlyBillableRate,
                        Address = request.Employee.Address.Trim(),
                        ZipCode = request.Employee.ZipCode.Trim(),
                        City = request.Employee.City.Trim(),
                        IsActive = request.Employee.IsActive,
                        Source = request.Employee.Source,
                        PaydayId = request.Employee.PaydayId
                    };

                    var user = new AppUser
                    {
                        CompanyId = userContext.CompanyId,
                        EmployeeId = employee.Id,
                        Email = normalizedEmail,
                        // Nobody holds this. The employee picks their own password from
                        // the emailed link, so no password is transmitted or displayed.
                        PasswordHash = PasswordHasher.HashPassword(invites.CreateUnusablePassword()),
                        Role = WorkitRoles.Employee
                    };

                    await employee.StampCreatedAsync(db, httpContext, userContext, ct);
                    db.Employees.Add(employee);
                    db.AppUsers.Add(user);
                    await db.SaveChangesAsync(ct);

                    await invites.SendInviteAsync(employee.Email, employee.DisplayName, InviteKind.Employee, ct);

                    return Results.Created($"/api/employees/{employee.Id}", employee);
                },
                logger,
                "creating an employee"))
            .WithName("CreateEmployee");

        securedApi.MapPut("/employees/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Employee employee, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (id != employee.Id)
                    {
                        return Results.BadRequest("Employee id mismatch.");
                    }

                    if (!IsValidEmployee(employee))
                    {
                        return Results.BadRequest("Employee name, SSN, and email are required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Employees.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    var normalizedEmail = employee.Email.Trim().ToLowerInvariant();
                    if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail && x.EmployeeId != id, ct))
                    {
                        return Results.Conflict("That email address is already in use.");
                    }

                    existing.DisplayName = employee.DisplayName.Trim();
                    existing.Trade = employee.Trade.Trim();
                    existing.Ssn = employee.Ssn.Trim();
                    existing.Email = normalizedEmail;
                    existing.Phone = employee.Phone.Trim();
                    existing.ContactPerson = employee.ContactPerson.Trim();
                    existing.EmploymentType = employee.EmploymentType;
                    existing.HourlySalary = employee.HourlySalary;
                    existing.HourlyBillableRate = employee.HourlyBillableRate;
                    existing.Address = employee.Address.Trim();
                    existing.ZipCode = employee.ZipCode.Trim();
                    existing.City = employee.City.Trim();
                    existing.IsActive = employee.IsActive;
                    existing.Source = employee.Source;
                    existing.PaydayId = employee.PaydayId;

                    var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.EmployeeId == id, ct);
                    if (appUser is not null)
                    {
                        appUser.Email = normalizedEmail;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating an employee"))
            .WithName("UpdateEmployee");

        // ── Send a fresh setup link to an employee whose invite lapsed ──
        securedApi.MapPost("/employees/{id:guid}/resend-invite", async (WorkitDbContext db, HttpContext httpContext, IAccountInviteService invites, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.EmployeeId == id && x.CompanyId == userContext.CompanyId, ct);
                    if (appUser is null)
                    {
                        return Results.NotFound();
                    }

                    if (DemoDataSeeder.IsProtectedAccount(appUser.Email))
                    {
                        return Results.BadRequest("This is a demo account. Its password cannot be changed.");
                    }

                    var employee = await db.Employees
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    var displayName = employee?.DisplayName is { Length: > 0 } name ? name : appUser.Email;

                    await invites.SendInviteAsync(appUser.Email, displayName, InviteKind.Employee, ct);
                    return Results.NoContent();
                },
                logger,
                "resending an employee invite"))
            .WithName("ResendEmployeeInvite");

        securedApi.MapPut("/employees/{id:guid}/password", async (WorkitDbContext db, HttpContext httpContext, Guid id, ResetPasswordRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                    {
                        return Results.BadRequest("Password must be at least 8 characters.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.EmployeeId == id && x.CompanyId == userContext.CompanyId, ct);
                    if (appUser is null)
                    {
                        return Results.NotFound();
                    }

                    if (DemoDataSeeder.IsProtectedAccount(appUser.Email))
                    {
                        return Results.BadRequest("This is a demo account. Its password cannot be changed.");
                    }

                    appUser.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "resetting employee password"))
            .WithName("ResetEmployeePassword");
    }
}
