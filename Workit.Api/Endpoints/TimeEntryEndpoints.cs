using Microsoft.EntityFrameworkCore;
using Workit.Api.Analytics;
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

        securedApi.MapPost("/timeentries", async (WorkitDbContext db, HttpContext httpContext, IAnalyticsService analytics, TimeEntry entry, CancellationToken ct) =>
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
                        if (await JobClosure.IsFinishedAsync(db, userContext.CompanyId, entry.JobId, ct))
                            return Results.Conflict(JobClosure.FinishedMessage);
                    }
                    else
                    {
                        return Results.Forbid();
                    }

                    var job = await FindUsableJobAsync(db, userContext, entry.JobId, ct);
                    if (job is null)
                        return Results.BadRequest("You are not assigned to that job.");

                    if (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, userContext.CompanyId, entry.JobId, entry.TaskId, ct) is string taskError)
                        return Results.BadRequest(taskError);

                    // A day already covered by absence has only so much room left.
                    if (await AbsenceDuty.DayOverflowAsync(db, entry.CompanyId, entry.EmployeeId, entry.WorkDate, entry.Hours, null, ct) is string full)
                        return Results.Conflict(full);

                    await entry.StampCreatedAsync(db, httpContext, userContext, ct);
                    db.TimeEntries.Add(entry);

                    // Stamp the job as In Progress the first time a time entry is added.
                    if (job.KanbanInProgressAt is null)
                    {
                        job.KanbanInProgressAt = DateTimeOffset.UtcNow;
                    }

                    await db.SaveChangesAsync(ct);

                    analytics.Capture(userContext.UserId.ToString(), "time_entry_created", new
                    {
                        company_id    = userContext.CompanyId,
                        source        = userContext.Role,
                        hours         = entry.Hours,
                        has_overtime  = entry.OvertimeHours > 0,
                        has_driving   = entry.DrivingUnits > 0,
                    });

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
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        if (existing.EmployeeId != userContext.EmployeeId)
                            return Results.Forbid();

                        // Billed hours are the invoice's record, not the crew's to
                        // restate — the same rule the delete path has always had.
                        if (existing.IsInvoiced)
                            return Results.Conflict("This entry has been invoiced and can't be changed. Ask the office if it is wrong.");

                        // Nor may they move hours onto a job that is already finished.
                        if (await JobClosure.IsFinishedAsync(db, userContext.CompanyId, entry.JobId, ct))
                            return Results.Conflict(JobClosure.FinishedMessage);
                    }

                    if (await JobTaskEndpoints.ValidateTaskForEntryAsync(db, userContext.CompanyId, entry.JobId, entry.TaskId, ct) is string taskError)
                        return Results.BadRequest(taskError);

                    // The entry being edited does not count against its own day.
                    if (await AbsenceDuty.DayOverflowAsync(db, userContext.CompanyId, existing.EmployeeId, entry.WorkDate, entry.Hours, existing.Id, ct) is string full)
                        return Results.Conflict(full);

                    // Owners may move an entry to the person who actually worked it;
                    // an employee's entries stay theirs.
                    if (httpContext.User.IsOwnerOrAdmin() && entry.EmployeeId != Guid.Empty && entry.EmployeeId != existing.EmployeeId)
                    {
                        var employeeExists = await db.Employees.AnyAsync(
                            e => e.Id == entry.EmployeeId && e.CompanyId == userContext.CompanyId, ct);
                        if (!employeeExists) return Results.BadRequest("Employee not found in this company.");
                        existing.EmployeeId = entry.EmployeeId;
                    }

                    existing.JobId         = entry.JobId;
                    existing.TaskId        = entry.TaskId;
                    existing.WorkDate      = entry.WorkDate;
                    existing.Hours         = entry.Hours;
                    existing.OvertimeHours = entry.OvertimeHours;
                    existing.DrivingUnits  = entry.DrivingUnits;
                    existing.Notes         = entry.Notes;
                    // Invoicing state is the server's: it is set by mark-invoiced and
                    // cleared by resetting the invoice. Taking it from the request let
                    // an edit that simply omitted the flags silently un-invoice an
                    // entry, putting billed hours back in the "to invoice" pile.

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a time entry"))
            .WithName("UpdateTimeEntry");

        // ── Delete — a mistaken entry, or hours that became an absence ──────────
        securedApi.MapDelete("/timeentries/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    // Employees can only delete their own entries, like editing.
                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                        existing.EmployeeId != userContext.EmployeeId)
                        return Results.Forbid();

                    // Hours already billed to a customer stay; the invoice is the record.
                    if (existing.IsInvoiced)
                        return Results.Conflict("This entry has been invoiced and can't be deleted.");

                    db.TimeEntries.Remove(existing);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "deleting a time entry"))
            .WithName("DeleteTimeEntry");

        securedApi.MapPost("/timeentries/mark-invoiced", async (WorkitDbContext db, HttpContext httpContext, IAnalyticsService analytics, MarkInvoicedRequest request, CancellationToken ct) =>
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

                    // Stamp KanbanDoneAt on any job whose entries are now all invoiced.
                    var affectedJobIds = entries.Select(e => e.JobId).Distinct().ToList();
                    var affectedJobs = await db.Jobs
                        .Where(j => affectedJobIds.Contains(j.Id) && j.CompanyId == userContext.CompanyId)
                        .ToListAsync(ct);
                    foreach (var job in affectedJobs)
                    {
                        var allInvoiced = await db.TimeEntries
                            .Where(e => e.JobId == job.Id && e.CompanyId == userContext.CompanyId)
                            .AllAsync(e => e.IsInvoiced, ct);
                        if (allInvoiced)
                            job.KanbanDoneAt = now;
                    }

                    await db.SaveChangesAsync(ct);

                    analytics.Capture(userContext.UserId.ToString(), "time_entries_invoiced", new
                    {
                        company_id   = userContext.CompanyId,
                        entry_count  = entries.Count,
                        job_count    = entries.Select(e => e.JobId).Distinct().Count(),
                    });

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

                    var affectedJobIds = entries.Select(e => e.JobId).Distinct().ToList();

                    foreach (var entry in entries)
                    {
                        entry.IsInvoiced = false;
                        entry.InvoicedAt = null;
                        entry.PaydayInvoiceNumber = null;
                    }

                    await db.SaveChangesAsync(ct);

                    // Clear KanbanDoneAt for any job that is no longer fully invoiced.
                    var affectedJobs = await db.Jobs
                        .Where(j => affectedJobIds.Contains(j.Id) && j.CompanyId == userContext.CompanyId)
                        .ToListAsync(ct);
                    foreach (var job in affectedJobs)
                        job.KanbanDoneAt = null;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { Reset = entries.Count });
                },
                logger,
                "unmarking time entries as invoiced"))
            .WithName("MarkTimeEntriesUninvoiced");
    }
}
