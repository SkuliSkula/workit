using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal sealed record UpdateKanbanStatusRequest(KanbanStatus Status, string? WaitingReason);

internal static class JobEndpoints
{
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

        securedApi.MapPost("/jobs", async (WorkitDbContext db, HttpContext httpContext, Job job, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    job.CompanyId = userContext.CompanyId;
                    job.Code = job.Code.Trim();
                    job.Name = job.Name.Trim();

                    db.Jobs.Add(job);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/jobs/{job.Id}", job);
                },
                logger,
                "creating a job"))
            .WithName("CreateJob");

        securedApi.MapPatch("/jobs/{id:guid}/kanban-status", async (WorkitDbContext db, HttpContext httpContext, Guid id, UpdateKanbanStatusRequest req, CancellationToken ct) =>
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

                    if (string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.Code))
                    {
                        return Results.BadRequest("Job name and code are required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    existing.CustomerId  = job.CustomerId;
                    existing.Code        = job.Code.Trim();
                    existing.Name        = job.Name.Trim();
                    existing.BillingType = job.BillingType;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a job"))
            .WithName("UpdateJob");
    }
}
