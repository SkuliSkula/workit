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
                IEmailService emailService,
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

                    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                    {
                        return Results.BadRequest("Employee password must be at least 8 characters.");
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
                        ContactPerson = request.Employee.ContactPerson.Trim()
                    };

                    var user = new AppUser
                    {
                        CompanyId = userContext.CompanyId,
                        EmployeeId = employee.Id,
                        Email = normalizedEmail,
                        PasswordHash = PasswordHasher.HashPassword(request.Password),
                        Role = WorkitRoles.Employee
                    };

                    db.Employees.Add(employee);
                    db.AppUsers.Add(user);
                    await db.SaveChangesAsync(ct);

                    await emailService.SendEmployeeWelcomeAsync(employee.DisplayName, employee.Email, request.Password);

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

                    appUser.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "resetting employee password"))
            .WithName("ResetEmployeePassword");
    }
}
