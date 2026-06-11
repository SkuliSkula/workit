using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal sealed record CreatePaydayExpenseLinkRequest(Guid PaydayExpenseId, Guid JobId, string? SnapshotJson = null);

internal static class PaydayExpenseLinkEndpoints
{
    internal static void MapPaydayExpenseLinkEndpoints(this WebApplication app)
    {
        var group  = app.MapGroup("/api").RequireAuthorization().WithTags("PaydayExpenseLinks");
        var logger = app.Logger;

        // GET /api/payday-expense-links?jobId={id}
        // GET /api/payday-expense-links?expenseId={id}
        group.MapGet("/payday-expense-links", async (
            Guid? jobId,
            Guid? expenseId,
            WorkitDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var user  = httpContext.User.ToUserContext();
            var query = db.PaydayExpenseLinks.Where(x => x.CompanyId == user.CompanyId);

            if (jobId.HasValue)     query = query.Where(x => x.JobId == jobId.Value);
            if (expenseId.HasValue) query = query.Where(x => x.PaydayExpenseId == expenseId.Value);

            var links = await query.ToListAsync(ct);
            return Results.Ok(links);
        }, logger, "loading expense links"));

        // POST /api/payday-expense-links
        group.MapPost("/payday-expense-links", async (
            CreatePaydayExpenseLinkRequest req,
            WorkitDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();

            var user = httpContext.User.ToUserContext();

            var job = await db.Jobs.FirstOrDefaultAsync(
                j => j.Id == req.JobId && j.CompanyId == user.CompanyId, ct);
            if (job is null) return Results.NotFound("Job not found.");

            // Upsert: replace any existing link for this expense
            var existing = await db.PaydayExpenseLinks.FirstOrDefaultAsync(x =>
                x.CompanyId == user.CompanyId && x.PaydayExpenseId == req.PaydayExpenseId, ct);
            if (existing is not null)
                db.PaydayExpenseLinks.Remove(existing);

            var link = new PaydayExpenseLink
            {
                CompanyId       = user.CompanyId,
                PaydayExpenseId = req.PaydayExpenseId,
                JobId           = req.JobId,
                LinkedAt        = DateTimeOffset.UtcNow,
                SnapshotJson    = req.SnapshotJson
            };

            db.PaydayExpenseLinks.Add(link);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/payday-expense-links/{link.Id}", link);
        }, logger, "linking expense to job"));

        // DELETE /api/payday-expense-links/{id}
        group.MapDelete("/payday-expense-links/{id:guid}", async (
            Guid id,
            WorkitDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();

            var user = httpContext.User.ToUserContext();
            var link = await db.PaydayExpenseLinks.FirstOrDefaultAsync(
                x => x.Id == id && x.CompanyId == user.CompanyId, ct);
            if (link is null) return Results.NotFound();

            db.PaydayExpenseLinks.Remove(link);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }, logger, "deleting expense link"));
    }
}
