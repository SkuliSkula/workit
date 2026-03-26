using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class TimeEntryEndpoints
{
    internal static void MapTimeEntryEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Time Entries");
        var logger = app.Logger;

        securedApi.MapGet("/timeentries", async (
                WorkitDbContext db,
                HttpContext httpContext,
                Guid? employeeId,
                Guid? jobId,
                DateOnly? from,
                DateOnly? to,
                CancellationToken ct) =>
            {
                var userContext = httpContext.User.ToUserContext();
                var query = db.TimeEntries.Where(x => x.CompanyId == userContext.CompanyId);

                if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                    || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                {
                    if (employeeId is not null)
                    {
                        query = query.Where(x => x.EmployeeId == employeeId.Value);
                    }
                }
                else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                {
                    if (userContext.EmployeeId is not Guid currentEmployeeId)
                    {
                        return Results.Forbid();
                    }

                    query = query.Where(x => x.EmployeeId == currentEmployeeId);
                }
                else
                {
                    return Results.Forbid();
                }

                if (jobId is not null)
                {
                    query = query.Where(x => x.JobId == jobId.Value);
                }

                if (from is not null)
                {
                    query = query.Where(x => x.WorkDate >= from.Value);
                }

                if (to is not null)
                {
                    query = query.Where(x => x.WorkDate <= to.Value);
                }

                return await ExecuteDbAsync(
                    async () => Results.Ok(await query.OrderByDescending(x => x.WorkDate).ToListAsync(ct)),
                    logger,
                    "loading time entries");
            })
            .WithName("GetTimeEntries");

        securedApi.MapPost("/timeentries", async (WorkitDbContext db, HttpContext httpContext, TimeEntry entry, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                        || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                    {
                        // Owner/Admin posts on behalf of any employee in their company.
                        entry.CompanyId = userContext.CompanyId;
                        var employeeExists = await db.Employees
                            .AnyAsync(x => x.Id == entry.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                        if (!employeeExists)
                            return Results.BadRequest("Employee not found in this company.");
                    }
                    else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                             userContext.EmployeeId is Guid currentEmployeeId)
                    {
                        entry.CompanyId = userContext.CompanyId;
                        entry.EmployeeId = currentEmployeeId;
                    }
                    else
                    {
                        return Results.Forbid();
                    }

                    db.TimeEntries.Add(entry);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/timeentries/{entry.Id}", entry);
                },
                logger,
                "creating a time entry"))
            .WithName("CreateTimeEntry");

        securedApi.MapPut("/timeentries/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, TimeEntry entry, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (id != entry.Id)
                        return Results.BadRequest("Time entry id mismatch.");

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    // Employees can only edit their own entries
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                        existing.EmployeeId != userContext.EmployeeId)
                        return Results.Forbid();

                    existing.JobId         = entry.JobId;
                    existing.WorkDate      = entry.WorkDate;
                    existing.Hours         = entry.Hours;
                    existing.OvertimeHours = entry.OvertimeHours;
                    existing.DrivingUnits  = entry.DrivingUnits;
                    existing.Notes         = entry.Notes;
                    existing.IsInvoiced          = entry.IsInvoiced;
                    existing.InvoicedAt          = entry.InvoicedAt;
                    existing.PaydayInvoiceNumber = entry.PaydayInvoiceNumber;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a time entry"))
            .WithName("UpdateTimeEntry");

        securedApi.MapPost("/timeentries/mark-invoiced", async (WorkitDbContext db, HttpContext httpContext, MarkInvoicedRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    if (request.Ids is null || request.Ids.Count == 0)
                        return Results.BadRequest("No time entry ids provided.");

                    var entries = await db.TimeEntries
                        .Where(x => x.CompanyId == userContext.CompanyId && request.Ids.Contains(x.Id))
                        .ToListAsync(ct);

                    var now = DateTimeOffset.UtcNow;
                    foreach (var entry in entries)
                    {
                        entry.IsInvoiced = true;
                        entry.InvoicedAt = now;
                        entry.PaydayInvoiceNumber = request.PaydayInvoiceNumber;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { Marked = entries.Count });
                },
                logger,
                "marking time entries as invoiced"))
            .WithName("MarkTimeEntriesInvoiced");

        securedApi.MapPost("/timeentries/mark-uninvoiced", async (WorkitDbContext db, HttpContext httpContext, MarkUninvoicedRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    var entries = await db.TimeEntries
                        .Where(x => x.CompanyId == userContext.CompanyId
                                  && x.IsInvoiced
                                  && x.PaydayInvoiceNumber == request.PaydayInvoiceNumber)
                        .ToListAsync(ct);

                    foreach (var entry in entries)
                    {
                        entry.IsInvoiced = false;
                        entry.InvoicedAt = null;
                        entry.PaydayInvoiceNumber = null;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { Reset = entries.Count });
                },
                logger,
                "unmarking time entries as invoiced"))
            .WithName("MarkTimeEntriesUninvoiced");
    }
}
