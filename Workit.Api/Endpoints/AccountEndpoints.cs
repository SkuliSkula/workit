using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// What a signed-in person may do to their own account from the apps: read and
/// correct their personal details, and delete the account.
///
/// Employment terms stay with the employer — pay rates, employment type, SSN,
/// trade, which company they work for and whether they are active are set in
/// the console, not here, and this endpoint ignores them if they are sent.
/// The sign-in email is the identity the invite was issued to, so it is
/// changed by an administrator rather than self-service.
/// </summary>
internal static class AccountEndpoints
{
    internal static void MapAccountEndpoints(this WebApplication app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization().WithTags("Account");
        var logger = app.Logger;

        me.MapGet("/", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var account = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == user.UserId, ct);
                    if (account is null) return Results.NotFound();

                    var employee = user.EmployeeId is Guid employeeId
                        ? await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == user.CompanyId, ct)
                        : null;
                    var company = await db.Companies.Where(c => c.Id == user.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct);

                    return Results.Ok(new MyAccount(
                        Name:        employee?.DisplayName ?? account.Name,
                        Email:       account.Email,
                        Phone:       employee?.Phone   ?? string.Empty,
                        Address:     employee?.Address ?? string.Empty,
                        ZipCode:     employee?.ZipCode ?? string.Empty,
                        City:        employee?.City    ?? string.Empty,
                        Trade:       employee?.Trade   ?? string.Empty,
                        CompanyName: company ?? string.Empty,
                        Role:        account.Role,
                        CanDelete:   await CanDeleteAccountAsync(db, user, ct)));
                }, logger, "loading the account"))
            .WithName("GetMyAccount");

        me.MapPut("/", async (WorkitDbContext db, HttpContext http, MyAccountUpdate body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                    await UpdateAsync(db, http.User.ToUserContext(), body, ct)
                        ? Results.NoContent()
                        : Results.BadRequest("Your name cannot be empty."),
                    logger, "updating the account"))
            .WithName("UpdateMyAccount");

        // Required by App Store guideline 5.1.1(v). This removes the person's
        // access to Workit: the sign-in, its refresh tokens and any pending
        // password reset. The employment record and the hours logged against it
        // stay with the employer, who has to keep them for payroll and is the
        // controller of that data — the apps say so before confirming.
        me.MapDelete("/", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    if (!await db.AppUsers.AnyAsync(u => u.Id == user.UserId, ct)) return Results.NotFound();

                    return await DeleteAsync(db, user, ct)
                        ? Results.NoContent()
                        : Results.Conflict(
                            "You are the only owner of this company, so deleting your account would leave it unreachable. "
                            + "Make someone else an owner first, or contact support@workit.is.");
                }, logger, "deleting the account"))
            .WithName("DeleteMyAccount");
    }

    /// <summary>
    /// Saves the person's own name and contact details. Returns false when the
    /// name is blank; everything else about the employment is left untouched.
    /// </summary>
    internal static async Task<bool> UpdateAsync(WorkitDbContext db, UserContext user, MyAccountUpdate body, CancellationToken ct)
    {
        var account = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == user.UserId, ct);
        if (account is null) return false;

        var name = (body.Name ?? string.Empty).Trim();
        if (name.Length == 0) return false;

        account.Name = name;

        if (user.EmployeeId is Guid employeeId)
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == user.CompanyId, ct);
            if (employee is not null)
            {
                employee.DisplayName = name;
                employee.Phone       = (body.Phone   ?? string.Empty).Trim();
                employee.Address     = (body.Address ?? string.Empty).Trim();
                employee.ZipCode     = (body.ZipCode ?? string.Empty).Trim();
                employee.City        = (body.City    ?? string.Empty).Trim();
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Removes the sign-in, its refresh tokens, pending password resets and
    /// company memberships, and deactivates the employment record. Returns
    /// false when the caller is the last owner of the company.
    /// </summary>
    internal static async Task<bool> DeleteAsync(WorkitDbContext db, UserContext user, CancellationToken ct)
    {
        var account = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == user.UserId, ct);
        if (account is null) return false;
        if (!await CanDeleteAccountAsync(db, user, ct)) return false;

        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => t.UserId == account.Id).ToListAsync(ct));
        db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(t => t.Email == account.Email).ToListAsync(ct));
        db.UserCompanies.RemoveRange(await db.UserCompanies.Where(c => c.UserId == account.Id).ToListAsync(ct));

        // The employment record is the employer's; it goes inactive so nobody is
        // assigned work, but the hours logged against it keep their owner.
        if (user.EmployeeId is Guid employeeId)
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == user.CompanyId, ct);
            if (employee is not null) employee.IsActive = false;
        }

        db.AppUsers.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// An employee can always delete. The last owner of a company cannot — that
    /// would leave the company with nobody able to administer it.
    /// </summary>
    internal static async Task<bool> CanDeleteAccountAsync(WorkitDbContext db, UserContext user, CancellationToken ct)
    {
        var isOwner = string.Equals(user.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                   || string.Equals(user.Role, WorkitRoles.Admin, StringComparison.Ordinal);
        if (!isOwner) return true;

        var otherOwners = await db.AppUsers.CountAsync(
            u => u.CompanyId == user.CompanyId && u.Id != user.UserId
              && (u.Role == WorkitRoles.Owner || u.Role == WorkitRoles.Admin), ct);
        return otherOwners > 0;
    }
}
