using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// Links Payday expenses to jobs from the job code a vendor wrote on the
/// invoice. Reads the company's expenses from Payday, looks for a code or a
/// job-number reference in the free-text fields, and links each expense
/// that points at exactly one job. An owner's decisions win: an expense
/// they linked is never re-pointed, and one they unlinked is never linked
/// again. Expenses naming several jobs are reported, not guessed.
/// </summary>
internal static class ExpenseAutoLinkEndpoints
{
    private const int PageSize = 100;
    /// <summary>Upper bound on Payday pages per pass — 1,000 expenses is far more than one owner's window.</summary>
    private const int MaxPages = 10;

    internal static void MapExpenseAutoLinkEndpoints(this WebApplication app)
    {
        var logger = app.Logger;

        app.MapPost("/api/expenses/auto-link", async (
                WorkitDbContext db, HttpContext httpContext, IPaydayExpensesApi payday,
                string? dateFrom, string? dateTo, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var expenses = new List<PaydayExpense>();
                    for (var page = 1; page <= MaxPages; page++)
                    {
                        var result = await payday.GetAllAsync(page, PageSize, "lines", dateFrom, dateTo);
                        if (!result.IsSuccess || result.Value is null)
                            return Results.Problem(result.ErrorMessage ?? "Could not read expenses from Payday.", statusCode: StatusCodes.Status502BadGateway);
                        expenses.AddRange(result.Value.Expenses);
                        if (page >= result.Value.Pages || result.Value.Expenses.Count == 0) break;
                    }

                    var jobs = await db.Jobs.AsNoTracking().Where(j => j.CompanyId == companyId).ToListAsync(ct);
                    var outcome = await LinkAsync(db, companyId, expenses, jobs, ct);

                    if (outcome.Linked.Count > 0)
                        logger.LogInformation("Auto-linked {Count} Payday expenses to jobs for company {CompanyId}.", outcome.Linked.Count, companyId);

                    return Results.Ok(outcome);
                }, logger, "auto-linking Payday expenses to jobs"))
            .RequireAuthorization()
            .WithTags("Expenses")
            .WithName("AutoLinkPaydayExpenses");
    }

    /// <summary>The matching and persisting half, separated from the Payday fetch so it can be tested with plain data.</summary>
    internal static async Task<ExpenseAutoLinkResult> LinkAsync(
        WorkitDbContext db, Guid companyId, IReadOnlyList<PaydayExpense> expenses, IReadOnlyCollection<Job> jobs, CancellationToken ct)
    {
        var result = new ExpenseAutoLinkResult { Scanned = expenses.Count };
        if (jobs.Count == 0 || expenses.Count == 0) return result;

        var paydayIds = expenses.Select(e => e.Id).ToList();
        var imported = await db.Expenses
            .Where(e => e.CompanyId == companyId && e.PaydayId != null && paydayIds.Contains(e.PaydayId.Value))
            .ToDictionaryAsync(e => e.PaydayId!.Value, ct);

        foreach (var pe in expenses)
        {
            imported.TryGetValue(pe.Id, out var existing);

            // The owner has spoken — either by linking or by unlinking.
            if (existing is not null &&
                (existing.JobId is not null || existing.JobLinkSource == ExpenseJobLinkSource.RemovedByOwner))
            {
                result.Skipped++;
                continue;
            }

            var found = JobCodeMatcher.FindJobs(JobCodeMatcher.TextsOf(pe), jobs);
            if (found.Count == 0) continue;
            if (found.Count > 1)
            {
                result.Ambiguous.Add(new ExpenseAutoLinkAmbiguity
                {
                    PaydayExpenseId = pe.Id,
                    CreditorName    = pe.Creditor?.Name,
                    JobCodes        = found.Select(j => j.Code).ToList(),
                });
                continue;
            }

            var job = found[0];
            if (existing is not null)
            {
                existing.JobId         = job.Id;
                existing.JobLinkSource = ExpenseJobLinkSource.Automatic;
            }
            else
            {
                var expense = pe.ToWorkitExpense(job.Id, companyId);
                expense.JobLinkSource = ExpenseJobLinkSource.Automatic;
                foreach (var line in expense.Lines)
                {
                    line.ExpenseId = expense.Id;
                    line.CompanyId = companyId;
                }
                db.Expenses.Add(expense);
            }

            result.Linked.Add(new ExpenseAutoLinkHit
            {
                PaydayExpenseId    = pe.Id,
                CreditorName       = pe.Creditor?.Name,
                AmountIncludingVat = pe.AmountIncludingVat,
                JobId              = job.Id,
                JobCode            = job.Code,
                JobName            = job.Name,
            });
        }

        if (result.Linked.Count > 0)
            await db.SaveChangesAsync(ct);

        return result;
    }
}
