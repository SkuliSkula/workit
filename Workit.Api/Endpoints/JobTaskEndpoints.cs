using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// Tasks inside a job. Owners and admins create, edit and delete them; an
/// employee sees the tasks on jobs they may use that either name them or name
/// nobody, and may mark those done. Numbers and codes are the API's to assign,
/// like job codes. A job with no tasks is unaffected by any of this.
/// </summary>
internal static class JobTaskEndpoints
{
    internal const int MaxTaskNumberAttempts = 5;

    internal static void MapJobTaskEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().WithTags("Tasks");
        var logger = app.Logger;

        // ── List ────────────────────────────────────────────────────────────────
        api.MapGet("/tasks", async (WorkitDbContext db, HttpContext httpContext, Guid? jobId, JobTaskStatus? status, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = httpContext.User.ToUserContext();
                    var query = db.JobTasks.AsNoTracking().Where(t => t.CompanyId == user.CompanyId);
                    if (jobId is Guid j) query = query.Where(t => t.JobId == j);
                    if (status is JobTaskStatus s) query = query.Where(t => t.Status == s);
                    var tasks = await query.OrderBy(t => t.JobId).ThenBy(t => t.SortOrder).ThenBy(t => t.TaskNumber).ToListAsync(ct);

                    if (string.Equals(user.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        var jobIds = tasks.Select(t => t.JobId).Distinct().ToList();
                        var jobs = await db.Jobs.AsNoTracking()
                            .Where(x => x.CompanyId == user.CompanyId && jobIds.Contains(x.Id))
                            .ToDictionaryAsync(x => x.Id, ct);
                        tasks = tasks.Where(t => jobs.TryGetValue(t.JobId, out var job) && user.CanSeeTask(job, t)).ToList();
                    }

                    return Results.Ok(tasks);
                }, logger, "listing tasks"))
            .WithName("GetTasks");

        // ── Create ──────────────────────────────────────────────────────────────
        api.MapPost("/jobs/{jobId:guid}/tasks", async (WorkitDbContext db, HttpContext httpContext, Guid jobId, JobTask task, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = httpContext.User.ToUserContext();

                    var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId && x.CompanyId == user.CompanyId, ct);
                    if (job is null) return Results.NotFound();

                    task.Name = task.Name.Trim();
                    if (task.Name.Length == 0) return Results.BadRequest("Task name is required.");
                    if (task.EstimatedHours is < 0) return Results.BadRequest("Estimated hours can't be negative.");

                    var assignees = await JobEndpoints.NormalizeAssigneesAsync(db, user.CompanyId, task.AssignedEmployeeIds, ct);
                    if (assignees is null) return Results.BadRequest("Employee not found.");

                    // The client may send an id (the apps do); everything else is ours.
                    task.CompanyId           = user.CompanyId;
                    task.JobId               = job.Id;
                    task.Description         = task.Description?.Trim() ?? string.Empty;
                    task.AssignedEmployeeIds = assignees;
                    task.Status              = JobTaskStatus.Open;
                    task.DoneAt              = null;
                    task.DoneByUserId        = null;
                    await task.StampCreatedAsync(db, httpContext, user, ct);

                    // Numbered within the job under the job's lock, same scheme as
                    // job numbers within a company; the unique index is the backstop.
                    db.JobTasks.Add(task);
                    for (var attempt = 1; ; attempt++)
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(ct);
                        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({JobEndpoints.JobNumberLockKey(job.Id)})", ct);

                        var next = (await db.JobTasks.Where(t => t.JobId == job.Id).MaxAsync(t => (int?)t.TaskNumber, ct) ?? 0) + 1;
                        task.TaskNumber = next;
                        task.Code       = FormatTaskCode(job.Code, next);
                        if (task.SortOrder == 0) task.SortOrder = next;

                        try
                        {
                            await db.SaveChangesAsync(ct);
                            await tx.CommitAsync(ct);
                            break;
                        }
                        catch (DbUpdateException ex) when (JobEndpoints.IsUniqueViolation(ex) && attempt < MaxTaskNumberAttempts)
                        {
                            await tx.RollbackAsync(ct);
                        }
                    }

                    return Results.Created($"/api/tasks/{task.Id}", task);
                }, logger, "creating a task"))
            .WithName("CreateTask");

        // ── Update ──────────────────────────────────────────────────────────────
        api.MapPut("/tasks/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, JobTask task, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (id != task.Id) return Results.BadRequest("Task id mismatch.");
                    var user = httpContext.User.ToUserContext();

                    var existing = await db.JobTasks.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == user.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    var name = task.Name.Trim();
                    if (name.Length == 0) return Results.BadRequest("Task name is required.");
                    if (task.EstimatedHours is < 0) return Results.BadRequest("Estimated hours can't be negative.");

                    var assignees = await JobEndpoints.NormalizeAssigneesAsync(db, user.CompanyId, task.AssignedEmployeeIds, ct);
                    if (assignees is null) return Results.BadRequest("Employee not found.");

                    // Number, code, job, status and audit are not the client's to change.
                    existing.Name                = name;
                    existing.Description         = task.Description?.Trim() ?? string.Empty;
                    existing.EstimatedHours      = task.EstimatedHours;
                    existing.AssignedEmployeeIds = assignees;
                    existing.SortOrder           = task.SortOrder;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                }, logger, "updating a task"))
            .WithName("UpdateTask");

        // ── Done / reopen ───────────────────────────────────────────────────────
        api.MapPost("/tasks/{id:guid}/done",   (WorkitDbContext db, HttpContext http, Guid id, CancellationToken ct) => SetDoneAsync(db, http, id, done: true,  ct, logger))
           .WithName("MarkTaskDone");
        api.MapPost("/tasks/{id:guid}/reopen", (WorkitDbContext db, HttpContext http, Guid id, CancellationToken ct) => SetDoneAsync(db, http, id, done: false, ct, logger))
           .WithName("ReopenTask");

        // ── Delete ──────────────────────────────────────────────────────────────
        api.MapDelete("/tasks/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = httpContext.User.ToUserContext();

                    var existing = await db.JobTasks.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == user.CompanyId, ct);
                    if (existing is null) return Results.NotFound();

                    var outcome = await DeleteTaskAsync(db, existing, ct);
                    return outcome is null
                        ? Results.NoContent()
                        : Results.Conflict(outcome);
                }, logger, "deleting a task"))
            .WithName("DeleteTask");
    }

    private static async Task<IResult> SetDoneAsync(WorkitDbContext db, HttpContext httpContext, Guid id, bool done, CancellationToken ct, ILogger logger) =>
        await ExecuteDbAsync(async () =>
        {
            var user = httpContext.User.ToUserContext();
            var task = await db.JobTasks.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == user.CompanyId, ct);
            if (task is null) return Results.NotFound();

            // Owners and admins always; an employee only for a task they can see,
            // and never on a job that is finished and invoiced.
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == task.JobId, ct);
                if (job is null || !user.CanSeeTask(job, task)) return Results.Forbid();
                if (await JobClosure.IsFinishedAsync(db, user.CompanyId, task.JobId, ct))
                    return Results.Conflict(JobClosure.FinishedMessage);
            }

            task.Status       = done ? JobTaskStatus.Done : JobTaskStatus.Open;
            task.DoneAt       = done ? DateTimeOffset.UtcNow : null;
            task.DoneByUserId = done ? user.UserId : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(task);
        }, logger, done ? "marking a task done" : "reopening a task");

    /// <summary>The task code: the job's code and the task's number, e.g. <c>MNT102-3</c>.</summary>
    internal static string FormatTaskCode(string jobCode, int taskNumber) => $"{jobCode}-{taskNumber}";

    /// <summary>
    /// Whether an employee may see (and log against, mark done) a task: they
    /// must be able to use the job, and the task must name them or name nobody.
    /// Owners and admins see every task.
    /// </summary>
    internal static bool CanSeeTask(this UserContext user, Job job, JobTask task)
    {
        if (!user.CanUseJob(job)) return false;
        if (!string.Equals(user.Role, WorkitRoles.Employee, StringComparison.Ordinal)) return true;
        if (task.AssignedEmployeeIds.Count == 0) return true;
        return user.EmployeeId is Guid me && task.AssignedEmployeeIds.Contains(me);
    }

    /// <summary>
    /// Deletes a task, letting its time entries keep their hours on the job.
    /// Returns null on success, or the reason it was refused: entries already
    /// on an invoice were grouped by this task, so it stays.
    /// </summary>
    internal static async Task<string?> DeleteTaskAsync(WorkitDbContext db, JobTask task, CancellationToken ct)
    {
        var entries = await db.TimeEntries.Where(e => e.TaskId == task.Id).ToListAsync(ct);
        if (entries.Any(e => e.IsInvoiced))
            return "This task has hours on an invoice and can't be deleted. Mark it done instead.";

        foreach (var e in entries) e.TaskId = null;
        db.JobTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// For a time entry that names a task: the task must exist in the company
    /// and belong to the entry's job. Returns an error message, or null when
    /// the entry is fine (including when it names no task).
    /// </summary>
    internal static async Task<string?> ValidateTaskForEntryAsync(WorkitDbContext db, Guid companyId, Guid jobId, Guid? taskId, CancellationToken ct)
    {
        if (taskId is not Guid id) return null;
        var task = await db.JobTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId, ct);
        if (task is null) return "Task not found.";
        if (task.JobId != jobId) return "That task belongs to a different job.";
        return null;
    }
}
