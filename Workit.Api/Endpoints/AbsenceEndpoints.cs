using Microsoft.EntityFrameworkCore;
using Workit.Api.Analytics;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class AbsenceEndpoints
{
    internal static void MapAbsenceEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Absences");
        var logger = app.Logger;

        securedApi.MapGet("/absences", async (
                WorkitDbContext db,
                HttpContext httpContext,
                Guid? employeeId,
                DateOnly? from,
                DateOnly? to,
                AbsenceStatus? status,
                CancellationToken ct) =>
            {
                var userContext = httpContext.User.ToUserContext();
                var query = db.AbsenceRequests.Where(x => x.CompanyId == userContext.CompanyId);

                if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                    || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                {
                    if (employeeId is not null)
                        query = query.Where(x => x.EmployeeId == employeeId.Value);
                }
                else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                {
                    if (userContext.EmployeeId is not Guid currentEmployeeId)
                        return Results.Forbid();
                    query = query.Where(x => x.EmployeeId == currentEmployeeId);
                }
                else
                {
                    return Results.Forbid();
                }

                if (from is not null) query = query.Where(x => x.StartDate >= from.Value);
                if (to is not null) query = query.Where(x => x.EndDate <= to.Value);
                if (status is not null) query = query.Where(x => x.Status == status.Value);

                return await ExecuteDbAsync(
                    async () => Results.Ok(await query.OrderByDescending(x => x.StartDate).ToListAsync(ct)),
                    logger,
                    "loading absences");
            })
            .WithName("GetAbsences");

        securedApi.MapPost("/absences", async (WorkitDbContext db, HttpContext httpContext, IAnalyticsService analytics, IEmailService emailService, AbsenceRequest absence, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    if (absence.StartDate > absence.EndDate)
                        return Results.BadRequest("Start date must be before or equal to end date.");

                    if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                        || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                    {
                        // Owner/Admin registers absence directly — auto-approved
                        absence.CompanyId = userContext.CompanyId;
                        absence.Status = AbsenceStatus.Approved;
                        absence.ReviewedBy = userContext.UserId;
                        absence.ReviewedAt = DateTime.UtcNow;

                        var employeeExists = await db.Employees
                            .AnyAsync(x => x.Id == absence.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                        if (!employeeExists)
                            return Results.BadRequest("Employee not found in this company.");
                    }
                    else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                             userContext.EmployeeId is Guid currentEmployeeId)
                    {
                        // Employee requests absence — starts as Pending
                        absence.CompanyId = userContext.CompanyId;
                        absence.EmployeeId = currentEmployeeId;
                        absence.Status = AbsenceStatus.Pending;
                        absence.ReviewedBy = null;
                        absence.ReviewedAt = null;
                    }
                    else
                    {
                        return Results.Forbid();
                    }

                    absence.CreatedAt = DateTime.UtcNow;
                    db.AbsenceRequests.Add(absence);
                    await db.SaveChangesAsync(ct);

                    analytics.Capture(userContext.UserId.ToString(), "absence_requested", new
                    {
                        company_id  = userContext.CompanyId,
                        source      = userContext.Role,
                        type        = absence.Type.ToString(),
                        days        = (absence.EndDate.DayNumber - absence.StartDate.DayNumber) + 1,
                    });

                    // Notify the owner when an employee submits a pending request
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        var ownerEmail = await db.AppUsers
                            .Where(u => u.Role == WorkitRoles.Owner && u.CompanyId == absence.CompanyId)
                            .Select(u => u.Email)
                            .FirstOrDefaultAsync(ct);

                        var employeeName = await db.Employees
                            .Where(e => e.Id == absence.EmployeeId)
                            .Select(e => e.DisplayName)
                            .FirstOrDefaultAsync(ct);

                        if (ownerEmail is not null && employeeName is not null)
                            await emailService.SendAbsenceRequestedAsync(ownerEmail, employeeName, absence.Type.ToString(), absence.StartDate, absence.EndDate);
                    }

                    return Results.Created($"/api/absences/{absence.Id}", absence);
                },
                logger,
                "creating an absence"))
            .WithName("CreateAbsence");

        securedApi.MapPut("/absences/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, AbsenceRequest absence, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (id != absence.Id)
                        return Results.BadRequest("Absence id mismatch.");

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    // Only pending absences can be edited
                    if (existing.Status != AbsenceStatus.Pending && !httpContext.User.IsOwnerOrAdmin())
                        return Results.BadRequest("Only pending absences can be edited.");

                    // Employees can only edit their own
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                        existing.EmployeeId != userContext.EmployeeId)
                        return Results.Forbid();

                    existing.Type = absence.Type;
                    existing.StartDate = absence.StartDate;
                    existing.EndDate = absence.EndDate;
                    existing.Notes = absence.Notes;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating an absence"))
            .WithName("UpdateAbsence");

        securedApi.MapDelete("/absences/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        // Employees can only cancel their own pending requests
                        if (existing.EmployeeId != userContext.EmployeeId)
                            return Results.Forbid();
                        if (existing.Status != AbsenceStatus.Pending)
                            return Results.BadRequest("Only pending requests can be cancelled.");

                        existing.Status = AbsenceStatus.Cancelled;
                        await db.SaveChangesAsync(ct);
                        return Results.Ok(existing);
                    }

                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    db.AbsenceRequests.Remove(existing);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "deleting an absence"))
            .WithName("DeleteAbsence");

        securedApi.MapPost("/absences/{id:guid}/review", async (WorkitDbContext db, HttpContext httpContext, IEmailService emailService, Guid id, AbsenceReviewPayload payload, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    if (existing.Status != AbsenceStatus.Pending)
                        return Results.BadRequest("Only pending absences can be reviewed.");

                    if (payload.Status != AbsenceStatus.Approved && payload.Status != AbsenceStatus.Denied)
                        return Results.BadRequest("Status must be Approved or Denied.");

                    existing.Status = payload.Status;
                    existing.ReviewedBy = userContext.UserId;
                    existing.ReviewedAt = DateTime.UtcNow;
                    existing.ReviewNotes = payload.ReviewNotes ?? string.Empty;

                    await db.SaveChangesAsync(ct);

                    var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == existing.EmployeeId, ct);
                    if (employee is not null)
                        await emailService.SendAbsenceReviewedAsync(
                            employee.Email, employee.DisplayName, existing.Type.ToString(),
                            existing.StartDate, existing.EndDate,
                            existing.Status == AbsenceStatus.Approved,
                            existing.ReviewNotes);

                    return Results.Ok(existing);
                },
                logger,
                "reviewing an absence"))
            .WithName("ReviewAbsence");
    }
}
