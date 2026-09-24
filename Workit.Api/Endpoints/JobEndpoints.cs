using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workit.Api.Analytics;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal sealed record UpdateKanbanStatusRequest(KanbanStatus Status, string? WaitingReason);

internal static class JobEndpoints
{
    /// <summary>See <see cref="JobCodes.Format"/>; kept as the API's entry point for tests and seeders.</summary>
    internal static string FormatJobCode(JobCategory category, int jobNumber) => JobCodes.Format(category, jobNumber);

    internal static void MapJobEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Jobs");
        var logger = app.Logger;

        securedApi.MapGet("/jobs", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var jobs = await db.Jobs
                        .Where(x => x.CompanyId == userContext.CompanyId)
                        .OrderBy(x => x.Code)
                        .ToListAsync(ct);
                    return Results.Ok(jobs);
                },
                logger,
                "loading jobs"))
            .WithName("GetJobs");

        // ── Address helper for the "For the crew" section ────────────────────
        securedApi.MapGet("/addresses/search", async (IAddressLookupService lookup, HttpContext httpContext, string? q, CancellationToken ct) =>
            {
                if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                return Results.Ok(await lookup.SearchAsync(q ?? string.Empty, ct));
            })
            .WithName("SearchAddresses");

        securedApi.MapGet("/jobs/contact-suggestions", async (WorkitDbContext db, HttpContext httpContext, int? landNr, string? location, Guid? customerId, Guid? excludeJobId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    return Results.Ok(await ContactSuggestionsAsync(db, userContext.CompanyId, landNr, location, customerId, excludeJobId, ct));
                },
                logger,
                "loading contact suggestions"))
            .WithName("GetContactSuggestions");

        securedApi.MapPost("/jobs", async (WorkitDbContext db, HttpContext httpContext, IAnalyticsService analytics, Job job, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    job.CompanyId = userContext.CompanyId;
                    job.Name      = job.Name.Trim();
                    TrimSiteDetails(job);

                    var customerExists = await db.Customers.AnyAsync(
                        c => c.Id == job.CustomerId && c.CompanyId == userContext.CompanyId, ct);
                    if (!customerExists)
                        return Results.BadRequest("Customer not found.");

                    var assignees = await NormalizeAssigneesAsync(db, userContext.CompanyId, job.AssignedEmployeeIds, ct);
                    if (assignees is null)
                        return Results.BadRequest("Employee not found.");
                    job.AssignedEmployeeIds = assignees;

                    // The job number is the company's MAX + 1. Creates in the same
                    // company take a per-company advisory lock for the transaction,
                    // so concurrent requests allocate one after another instead of
                    // all reading the same MAX. Should a collision get through
                    // anyway, the unique (CompanyId, JobNumber) index rejects it
                    // and the loser re-reads and tries again.
                    await job.StampCreatedAsync(db, httpContext, userContext, ct);
                    db.Jobs.Add(job);
                    for (var attempt = 1; ; attempt++)
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(ct);
                        await db.Database.ExecuteSqlAsync(
                            $"SELECT pg_advisory_xact_lock({JobNumberLockKey(userContext.CompanyId)})", ct);

                        var nextNumber = await NextJobNumberAsync(db, userContext.CompanyId, ct);
                        job.JobNumber = nextNumber;
                        job.Code      = FormatJobCode(job.Category, nextNumber);

                        try
                        {
                            await db.SaveChangesAsync(ct);
                            await tx.CommitAsync(ct);
                            break;
                        }
                        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < MaxJobNumberAttempts)
                        {
                            await tx.RollbackAsync(ct);
                            logger.LogInformation(
                                "Job number {JobNumber} was taken concurrently in company {CompanyId}; retrying ({Attempt}/{Max}).",
                                nextNumber, userContext.CompanyId, attempt, MaxJobNumberAttempts);
                        }
                    }

                    analytics.Capture(userContext.UserId.ToString(), "job_created", new
                    {
                        company_id   = userContext.CompanyId,
                        category     = job.Category.ToString(),
                        billing_type = job.BillingType.ToString(),
                    });

                    return Results.Created($"/api/jobs/{job.Id}", job);
                },
                logger,
                "creating a job"))
            .WithName("CreateJob");

        securedApi.MapPatch("/jobs/{id:guid}/kanban-status", async (WorkitDbContext db, HttpContext httpContext, IAnalyticsService analytics, Guid id, UpdateKanbanStatusRequest req, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    // The crew hits the reasons to wait — missing parts, no access —
                    // so whoever is on the job may park it, not just the office.
                    if (!httpContext.User.IsOwnerOrAdmin() && !IsAssignedTo(existing, userContext))
                    {
                        return Results.Forbid();
                    }

                    var now = DateTimeOffset.UtcNow;
                    existing.KanbanStatus  = req.Status;
                    existing.WaitingReason = req.Status == KanbanStatus.Waiting ? req.WaitingReason?.Trim() : null;

                    if (req.Status == KanbanStatus.Waiting)
                    {
                        existing.KanbanWaitingAt = now;
                    }
                    else
                    {
                        existing.KanbanWaitingAt = null;
                    }

                    await db.SaveChangesAsync(ct);

                    analytics.Capture(userContext.UserId.ToString(), "job_status_changed", new
                    {
                        company_id     = userContext.CompanyId,
                        job_id         = id,
                        new_status     = req.Status.ToString(),
                        waiting_reason = req.WaitingReason,
                    });

                    return Results.Ok(existing);
                },
                logger,
                "updating kanban status"))
            .WithName("UpdateJobKanbanStatus");

        // What to bring is the crew's own list — whoever is on the job keeps it
        // up to date from site, without touching anything else on the job.
        securedApi.MapPut("/jobs/{id:guid}/bring", async (WorkitDbContext db, HttpContext httpContext, Guid id, JobBringUpdate body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    if (!httpContext.User.IsOwnerOrAdmin() && !IsAssignedTo(existing, userContext))
                        return Results.Forbid();

                    existing.ToolsSuggestion     = (body.ToolsSuggestion ?? string.Empty).Trim();
                    existing.MaterialsSuggestion = (body.MaterialsSuggestion ?? string.Empty).Trim();
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating what to bring"))
            .WithName("UpdateJobBring");

        securedApi.MapPut("/jobs/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Job job, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (id != job.Id)
                    {
                        return Results.BadRequest("Job id mismatch.");
                    }

                    if (string.IsNullOrWhiteSpace(job.Name))
                    {
                        return Results.BadRequest("Job name is required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    var assignees = await NormalizeAssigneesAsync(db, userContext.CompanyId, job.AssignedEmployeeIds, ct);
                    if (assignees is null)
                        return Results.BadRequest("Employee not found.");

                    // Code, Category and JobNumber are set at creation and never change.
                    existing.CustomerId          = job.CustomerId;
                    existing.AssignedEmployeeIds = assignees;
                    existing.Name                = job.Name.Trim();
                    existing.BillingType         = job.BillingType;
                    TrimSiteDetails(job);
                    existing.Location            = job.Location;
                    existing.ContactName         = job.ContactName;
                    existing.ContactPhone        = job.ContactPhone;
                    existing.Instructions        = job.Instructions;
                    existing.ToolsSuggestion     = job.ToolsSuggestion;
                    existing.MaterialsSuggestion = job.MaterialsSuggestion;
                    existing.Latitude            = job.Latitude;
                    existing.Longitude           = job.Longitude;
                    existing.AddressLandNr       = job.AddressLandNr;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a job"))
            .WithName("UpdateJob");

        securedApi.MapDelete("/jobs/{id:guid}", async (WorkitDbContext db, IFileStorageService storage, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    var outcome = await DeleteJobAsync(db, existing, ct);
                    if (outcome.Refusal is not null)
                        return Results.Conflict(outcome.Refusal);

                    // Best-effort blob cleanup; the rows are already gone.
                    foreach (var key in outcome.StorageKeys)
                    {
                        try
                        {
                            await storage.DeleteAsync(key, ct);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to delete stored file {Key} for deleted job {JobId}.", key, id);
                        }
                    }

                    return Results.NoContent();
                },
                logger,
                "deleting a job"))
            .WithName("DeleteJob");
    }

    /// <summary>What deleting a job left to do, or why it was refused.</summary>
    internal sealed record DeleteJobOutcome(string? Refusal, IReadOnlyList<string> StorageKeys)
    {
        public static DeleteJobOutcome Refused(string why) => new(why, []);
    }

    /// <summary>
    /// Deletes a job that was created by mistake. A job with hours, materials
    /// or billed expenses on it is history and is refused — those rows carry
    /// the company's money; delete or move them first. Tasks and files go with
    /// the job (their hours are blocked above), and expenses that were linked
    /// to it are unlinked, since the link would otherwise point at nothing.
    /// </summary>
    internal static async Task<DeleteJobOutcome> DeleteJobAsync(WorkitDbContext db, Job job, CancellationToken ct)
    {
        var entryCount = await db.TimeEntries.CountAsync(e => e.JobId == job.Id, ct);
        if (entryCount > 0)
            return DeleteJobOutcome.Refused(entryCount == 1
                ? "This job has a time entry and can't be deleted. Delete or move that entry first."
                : $"This job has {entryCount} time entries and can't be deleted. Delete or move those entries first.");

        var usageCount = await db.MaterialUsages.CountAsync(u => u.JobId == job.Id, ct);
        if (usageCount > 0)
            return DeleteJobOutcome.Refused(usageCount == 1
                ? "This job has a material entry and can't be deleted. Delete that entry first."
                : $"This job has {usageCount} material entries and can't be deleted. Delete those entries first.");

        if (await db.ExpenseLineBillings.AnyAsync(b => b.JobId == job.Id, ct))
            return DeleteJobOutcome.Refused("Expenses have been billed to this job on an invoice, so it can't be deleted.");

        var tasks       = await db.JobTasks.Where(t => t.JobId == job.Id).ToListAsync(ct);
        var attachments = await db.JobAttachments.Where(a => a.JobId == job.Id).ToListAsync(ct);
        var expenses    = await db.Expenses.Where(e => e.JobId == job.Id).ToListAsync(ct);

        foreach (var expense in expenses)
        {
            expense.JobId         = null;
            expense.JobLinkSource = ExpenseJobLinkSource.None;
        }
        db.JobTasks.RemoveRange(tasks);
        db.JobAttachments.RemoveRange(attachments);
        db.Jobs.Remove(job);
        await db.SaveChangesAsync(ct);

        return new DeleteJobOutcome(null, attachments.Select(a => a.StorageKey).ToList());
    }

    /// <summary>
    /// Returns the assignee list deduplicated and in a stable order, or null if
    /// any id is not one of the caller's own employees. The ids come from the
    /// client, so a guid from another tenant must not be storable — and a
    /// duplicate must not make one person count twice.
    /// </summary>
    /// <summary>How many times CreateJob re-reads MAX + 1 after losing a race for a number.</summary>
    internal const int MaxJobNumberAttempts = 5;

    /// <summary>
    /// Key for the per-company advisory lock that serialises job-number
    /// allocation. Advisory locks take a bigint, so this is the first eight
    /// bytes of the company id; a collision between companies only means they
    /// queue behind each other for a moment.
    /// </summary>
    internal static long JobNumberLockKey(Guid companyId) =>
        BitConverter.ToInt64(companyId.ToByteArray(), 0);

    /// <summary>The next job number for a company: one past the highest in use, starting at 1.</summary>
    private static async Task<int> NextJobNumberAsync(WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        (await db.Jobs
            .Where(j => j.CompanyId == companyId)
            .MaxAsync(j => (int?)j.JobNumber, ct) ?? 0) + 1;

    /// <summary>True when a save failed on a unique index (Postgres SQLSTATE 23505).</summary>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static async Task<List<Guid>?> NormalizeAssigneesAsync(WorkitDbContext db, Guid companyId, List<Guid>? requested, CancellationToken ct)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return ids;

        var known = await db.Employees
            .Where(e => e.CompanyId == companyId && ids.Contains(e.Id))
            .CountAsync(ct);

        return known == ids.Count ? ids : null;
    }

    /// <summary>An employee only acts on jobs they are on; owners act on all of them.</summary>
    private static bool IsAssignedTo(Job job, UserContext user) =>
        user.EmployeeId is Guid employeeId && job.AssignedEmployeeIds.Contains(employeeId);

    private static void TrimSiteDetails(Job job)
    {
        job.Location        = (job.Location ?? string.Empty).Trim();
        job.ContactName     = (job.ContactName ?? string.Empty).Trim();
        job.ContactPhone    = (job.ContactPhone ?? string.Empty).Trim();
        job.Instructions    = (job.Instructions ?? string.Empty).Trim();
        job.ToolsSuggestion = (job.ToolsSuggestion ?? string.Empty).Trim();
        job.MaterialsSuggestion = (job.MaterialsSuggestion ?? string.Empty).Trim();
        // A pin without a location text is still a place; a pin off the globe is a bug.
        if (job.Latitude is < -90 or > 90 || job.Longitude is < -180 or > 180 || job.Latitude is null != job.Longitude is null)
        {
            job.Latitude = null;
            job.Longitude = null;
        }
    }

    /// <summary>
    /// Contacts to offer for a job at this address: people entered on earlier
    /// jobs on the same parcel (or with the same location text), newest first,
    /// then the customer's own contact person. Owners only — it reads across
    /// the company's jobs.
    /// </summary>
    internal static async Task<List<ContactSuggestion>> ContactSuggestionsAsync(
        WorkitDbContext db, Guid companyId, int? landNr, string? location, Guid? customerId, Guid? excludeJobId, CancellationToken ct)
    {
        var loc = (location ?? string.Empty).Trim().ToLower();
        var suggestions = new List<ContactSuggestion>();
        var seen = new HashSet<string>();

        if (landNr is not null || loc.Length >= 3)
        {
            var jobs = await db.Jobs
                .Where(j => j.CompanyId == companyId && j.Id != excludeJobId && j.ContactName != "")
                .Where(j => (landNr != null && j.AddressLandNr == landNr) || (loc != "" && j.Location.ToLower() == loc))
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new { j.Code, j.ContactName, j.ContactPhone, j.CreatedAt })
                .Take(50)
                .ToListAsync(ct);
            foreach (var j in jobs)
            {
                if (!seen.Add(Key(j.ContactName, j.ContactPhone))) continue;
                suggestions.Add(new ContactSuggestion(j.ContactName, j.ContactPhone, $"{j.Code} · {j.CreatedAt:d MMM yyyy}"));
                if (suggestions.Count == 5) break;
            }
        }

        if (customerId is Guid cid)
        {
            var c = await db.Customers.Where(x => x.Id == cid && x.CompanyId == companyId)
                .Select(x => new { x.Name, x.ContactPerson, x.Phone }).FirstOrDefaultAsync(ct);
            if (c is not null)
            {
                var name = string.IsNullOrWhiteSpace(c.ContactPerson) ? c.Name : c.ContactPerson;
                if (!string.IsNullOrWhiteSpace(name) && (!string.IsNullOrWhiteSpace(c.Phone) || !string.IsNullOrWhiteSpace(c.ContactPerson))
                    && seen.Add(Key(name, c.Phone)))
                    suggestions.Add(new ContactSuggestion(name.Trim(), c.Phone.Trim(), "Customer"));
            }
        }
        return suggestions;

        static string Key(string name, string phone) => name.Trim().ToLowerInvariant() + "|" + new string(phone.Where(char.IsDigit).ToArray());
    }
}
