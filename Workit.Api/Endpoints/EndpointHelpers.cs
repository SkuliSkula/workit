using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Endpoints;

internal static class EndpointHelpers
{
    /// <summary>
    /// Whether the caller may log work against a job. Owners and admins may
    /// use any job in their company. An employee may use only a job that
    /// names them; a job with no assignees is open to nobody until someone
    /// is put on it. The apps filter their pickers by the same rule; this is
    /// what makes the filter real rather than cosmetic.
    /// </summary>
    internal static bool CanUseJob(this UserContext user, Job job) =>
        !string.Equals(user.Role, WorkitRoles.Employee, StringComparison.Ordinal)
        || (user.EmployeeId is Guid me && job.AssignedEmployeeIds.Contains(me));

    /// <summary>
    /// Records who is creating <paramref name="record"/> and when. Overwrites
    /// whatever the client sent — these are the API's to set.
    /// </summary>
    internal static async Task StampCreatedAsync(
        this ICreatedAudit record, WorkitDbContext db, HttpContext httpContext, UserContext user, CancellationToken ct)
    {
        record.CreatedAt       = DateTimeOffset.UtcNow;
        record.CreatedByUserId = user.UserId;
        record.CreatedByName   = await ResolveDisplayNameAsync(db, httpContext, user, ct);
    }

    /// <summary>
    /// The caller's display name: the AppUser's name, else their Employee
    /// record's (employee logins have no AppUser name), else their email.
    /// </summary>
    internal static async Task<string> ResolveDisplayNameAsync(
        WorkitDbContext db, HttpContext httpContext, UserContext user, CancellationToken ct)
    {
        var name = await db.AppUsers
            .Where(u => u.Id == user.UserId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        if (user.EmployeeId is Guid employeeId)
        {
            var displayName = await db.Employees
                .Where(e => e.Id == employeeId)
                .Select(e => e.DisplayName)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }

        var email = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? "Unknown" : email;
    }

    /// <summary>The job if it is in the caller's company and they may use it; otherwise null.</summary>
    internal static async Task<Job?> FindUsableJobAsync(WorkitDbContext db, UserContext user, Guid jobId, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.CompanyId == user.CompanyId, ct);
        return job is not null && user.CanUseJob(job) ? job : null;
    }

    internal static async Task<IResult> ExecuteDbAsync(
        Func<Task<IResult>> action,
        ILogger logger,
        string operation)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Authorization context invalid while {Operation}.", operation);
            return Results.Unauthorized();
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            logger.LogError(ex, "Database unavailable while {Operation}.", operation);
            return Results.Problem(
                title: "Database unavailable",
                detail: $"The API could not access the database while {operation}. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static bool IsDatabaseException(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is DbException or DbUpdateException or TimeoutException)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Prepares a Company for an API response: returns a copy with the stored Payday
    /// credentials replaced by a boolean flag, so the values never leave the API.
    /// Returns a new instance so a tracked entity is never modified.
    /// Call this on every Company the API returns.
    /// </summary>
    internal static Workit.Shared.Models.Company WithoutSecrets(this Workit.Shared.Models.Company company) =>
        new()
        {
            Id                  = company.Id,
            Name                = company.Name,
            Ssn                 = company.Ssn,
            Email               = company.Email,
            Address             = company.Address,
            Phone               = company.Phone,
            Owner               = company.Owner,
            DrivingUnitPrice    = company.DrivingUnitPrice,
            StandardHoursPerDay = company.StandardHoursPerDay,
            ZipCode             = company.ZipCode,
            City                = company.City,
            VatNumber           = company.VatNumber,
            PayrollRegularItemName   = company.PayrollRegularItemName,
            PayrollOvertimeItemName  = company.PayrollOvertimeItemName,
            PayrollDrivingItemName   = company.PayrollDrivingItemName,
            MaterialsManagedInPayday = company.MaterialsManagedInPayday,
            Source              = company.Source,
            PaydayId            = company.PaydayId,
            PaydayClientId      = null,
            PaydayClientSecret  = null,
            HasPaydayCredentials =
                !string.IsNullOrWhiteSpace(company.PaydayClientId) &&
                !string.IsNullOrWhiteSpace(company.PaydayClientSecret)
        };

    internal static bool IsValidCompany(Workit.Shared.Models.Company company) =>
        !string.IsNullOrWhiteSpace(company.Name) &&
        !string.IsNullOrWhiteSpace(company.Ssn) &&
        !string.IsNullOrWhiteSpace(company.Email) &&
        !string.IsNullOrWhiteSpace(company.Address) &&
        !string.IsNullOrWhiteSpace(company.Phone) &&
        !string.IsNullOrWhiteSpace(company.Owner);

    internal static bool IsValidCustomer(Workit.Shared.Models.Customer customer) =>
        !string.IsNullOrWhiteSpace(customer.Name) &&
        !string.IsNullOrWhiteSpace(customer.Ssn);

    internal static bool IsValidEmployee(Workit.Shared.Models.Employee employee) =>
        !string.IsNullOrWhiteSpace(employee.DisplayName) &&
        !string.IsNullOrWhiteSpace(employee.Ssn) &&
        !string.IsNullOrWhiteSpace(employee.Email);

    internal static bool IsValidCredentials(string email, string password) =>
        !string.IsNullOrWhiteSpace(email) &&
        !string.IsNullOrWhiteSpace(password) &&
        password.Trim().Length >= 8;

    internal static string GenerateOwnerPassword()
    {
        var raw = Guid.NewGuid().ToString("N");
        return char.ToUpper(raw[0]) + raw[1..10] + "!1";
    }
}
