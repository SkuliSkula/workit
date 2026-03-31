using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal sealed record UpdateKanbanStatusRequest(KanbanStatus Status, string? WaitingReason);

internal static class JobEndpoints
{
    private static string GetCategoryCode(JobCategory category) => category switch
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

    private static string GetCustomerInitials(string customerName)
    {
        var words = customerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return customerName.Length >= 3
                ? customerName[..3].ToUpperInvariant()
                : customerName.ToUpperInvariant();
        return string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
    }

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
                    job.Name      = job.Name.Trim();

                    // Look up customer name for initials
                    var customer = await db.Customers.FirstOrDefaultAsync(
                        c => c.Id == job.CustomerId && c.CompanyId == userContext.CompanyId, ct);
                    if (customer is null)
                        return Results.BadRequest("Customer not found.");

                    // Assign globally unique sequential job number for this company
                    var nextNumber = (await db.Jobs
                        .Where(j => j.CompanyId == userContext.CompanyId)
                        .MaxAsync(j => (int?)j.JobNumber, ct) ?? 0) + 1;

                    job.JobNumber = nextNumber;
                    job.Code      = $"{GetCategoryCode(job.Category)}-{GetCustomerInitials(customer.Name)}-{nextNumber:D3}";

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

                    // Code, Category and JobNumber are set at creation and never change.
                    existing.CustomerId  = job.CustomerId;
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
