using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workit.Api.Analytics;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal sealed record UpdateKanbanStatusRequest(KanbanStatus Status, string? WaitingReason);

internal static class JobEndpoints
{
    /// <summary>
    /// The job code: the category's short code and the company's running job
    /// number, e.g. <c>REP007</c>. Assigned by the API on create, never edited.
    /// </summary>
    internal static string FormatJobCode(JobCategory category, int jobNumber) =>
        $"{GetCategoryCode(category)}{jobNumber:D3}";

    internal static string GetCategoryCode(JobCategory category) => category switch
    {
        JobCategory.NewInstallation => "NI",
        JobCategory.Repair          => "REP",
        JobCategory.InnerWork       => "IW",
        JobCategory.Drawings        => "DWG",
        JobCategory.Offer           => "OFF",
        JobCategory.Maintenance     => "MNT",
        JobCategory.Inspection      => "INS",
        JobCategory.Consultation    => "CON",
        _                           => "JOB"
    };

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

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a job"))
            .WithName("UpdateJob");
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

    private static async Task<List<Guid>?> NormalizeAssigneesAsync(WorkitDbContext db, Guid companyId, List<Guid>? requested, CancellationToken ct)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return ids;

        var known = await db.Employees
            .Where(e => e.CompanyId == companyId && ids.Contains(e.Id))
            .CountAsync(ct);

        return known == ids.Count ? ids : null;
    }
}
