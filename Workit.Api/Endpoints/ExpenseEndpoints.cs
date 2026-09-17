using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class ExpenseEndpoints
{
    internal static void MapExpenseEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Expenses");
        var logger = app.Logger;

        // ── List ─────────────────────────────────────────────────────────────
        securedApi.MapGet("/expenses", async (
                WorkitDbContext db, HttpContext httpContext,
                string? status, Guid? jobId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var query = db.Expenses.Where(x => x.CompanyId == companyId);
                    if (!string.IsNullOrWhiteSpace(status))
                        query = query.Where(x => x.Status == status);
                    if (jobId.HasValue)
                        query = query.Where(x => x.JobId == jobId);

                    var expenses = await query
                        .OrderByDescending(x => x.Date)
                        .ThenByDescending(x => x.CreatedAt)
                        .ToListAsync(ct);

                    // When scoped to a job, include lines + their billings so the
                    // caller (JobDetails / invoice builder) sees billed-vs-remaining.
                    if (jobId.HasValue && expenses.Count > 0)
                    {
                        var expIds = expenses.Select(e => e.Id).ToList();
                        var lines = await db.ExpenseLines
                            .Where(l => expIds.Contains(l.ExpenseId) && l.CompanyId == companyId)
                            .ToListAsync(ct);
                        var lineIds = lines.Select(l => l.Id).ToList();
                        var billings = await db.ExpenseLineBillings
                            .Where(b => lineIds.Contains(b.ExpenseLineId) && b.CompanyId == companyId)
                            .ToListAsync(ct);

                        foreach (var line in lines)
                            line.Billings = billings.Where(b => b.ExpenseLineId == line.Id).ToList();
                        foreach (var expense in expenses)
                            expense.Lines = lines.Where(l => l.ExpenseId == expense.Id).ToList();
                    }

                    return Results.Ok(expenses);
                }, logger, "loading expenses"))
            .WithName("GetExpenses");

        // ── Get by id (with lines) ────────────────────────────────────────────
        securedApi.MapGet("/expenses/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var expense = await db.Expenses
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (expense is null) return Results.NotFound();

                    expense.Lines = await db.ExpenseLines
                        .Where(x => x.ExpenseId == id)
                        .ToListAsync(ct);

                    var lineIds = expense.Lines.Select(l => l.Id).ToList();
                    var billings = await db.ExpenseLineBillings
                        .Where(b => lineIds.Contains(b.ExpenseLineId) && b.CompanyId == companyId)
                        .ToListAsync(ct);
                    foreach (var line in expense.Lines)
                        line.Billings = billings.Where(b => b.ExpenseLineId == line.Id).ToList();

                    return Results.Ok(expense);
                }, logger, "loading expense"))
            .WithName("GetExpense");

        // ── Import a Payday expense into the Workit DB (single source of truth) ─
        // Idempotent: deduped by PaydayId so each Payday expense is imported once.
        securedApi.MapPost("/expenses/import-payday", async (
                WorkitDbContext db, HttpContext httpContext,
                Expense expense, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    if (expense.PaydayId is null)
                        return Results.BadRequest("PaydayId is required to import a Payday expense.");

                    var existing = await db.Expenses
                        .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.PaydayId == expense.PaydayId, ct);

                    if (existing is not null)
                    {
                        // Already imported — only (re)link to a job, never overwrite lines/billings.
                        if (expense.JobId.HasValue && existing.JobId != expense.JobId)
                        {
                            existing.JobId         = expense.JobId;
                            existing.JobLinkSource = ExpenseJobLinkSource.Manual;
                            await db.SaveChangesAsync(ct);
                        }
                        existing.Lines = await db.ExpenseLines
                            .Where(l => l.ExpenseId == existing.Id)
                            .ToListAsync(ct);
                        return Results.Ok(existing);
                    }

                    expense.CompanyId = companyId;
                    expense.Source    = DataSource.Payday;
                    expense.CreatedAt = DateTimeOffset.UtcNow;
                    expense.JobLinkSource = expense.JobId.HasValue ? ExpenseJobLinkSource.Manual : ExpenseJobLinkSource.None;

                    foreach (var line in expense.Lines)
                    {
                        line.ExpenseId = expense.Id;
                        line.CompanyId = companyId;
                        if (line.MarkupFactor <= 0) line.MarkupFactor = 1.5m;
                        if (line.SalePriceExcludingVat <= 0)
                            line.SalePriceExcludingVat = Math.Round((line.UnitPriceExcludingVat ?? 0m) * line.MarkupFactor, 0, MidpointRounding.AwayFromZero);
                    }

                    db.Expenses.Add(expense);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/expenses/{expense.Id}", expense);
                }, logger, "importing Payday expense"))
            .WithName("ImportPaydayExpense");

        // ── Bill expense lines onto an invoice (partial or full) ───────────────
        securedApi.MapPost("/expenses/lines/bill", async (
                WorkitDbContext db, HttpContext httpContext,
                BillExpenseLinesRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var lineIds = request.Lines.Select(l => l.ExpenseLineId).Distinct().ToList();
                    var lines = await db.ExpenseLines
                        .Where(l => lineIds.Contains(l.Id) && l.CompanyId == companyId)
                        .ToListAsync(ct);
                    var existingBillings = await db.ExpenseLineBillings
                        .Where(b => lineIds.Contains(b.ExpenseLineId) && b.CompanyId == companyId)
                        .ToListAsync(ct);

                    foreach (var entry in request.Lines)
                    {
                        var line = lines.FirstOrDefault(l => l.Id == entry.ExpenseLineId);
                        if (line is null) continue;

                        var alreadyBilled = existingBillings
                            .Where(b => b.ExpenseLineId == entry.ExpenseLineId)
                            .Sum(b => b.Quantity);
                        var remaining = line.Quantity - alreadyBilled;
                        var qty = Math.Min(entry.Quantity, remaining);
                        if (qty <= 0) continue;

                        db.ExpenseLineBillings.Add(new ExpenseLineBilling
                        {
                            CompanyId             = companyId,
                            ExpenseLineId         = entry.ExpenseLineId,
                            JobId                 = entry.JobId,
                            Quantity              = qty,
                            SalePriceExcludingVat = entry.SalePriceExcludingVat,
                            VatPercentage         = entry.VatPercentage,
                            PaydayInvoiceNumber   = request.PaydayInvoiceNumber,
                            InvoiceLineId         = entry.InvoiceLineId,
                            BilledAt              = DateTimeOffset.UtcNow
                        });
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok();
                }, logger, "billing expense lines"))
            .WithName("BillExpenseLines");

        // ── Create ────────────────────────────────────────────────────────────
        securedApi.MapPost("/expenses", async (
                WorkitDbContext db, HttpContext httpContext,
                Expense expense, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    expense.CompanyId = companyId;
                    expense.CreatedAt = DateTimeOffset.UtcNow;

                    foreach (var line in expense.Lines)
                    {
                        line.ExpenseId = expense.Id;
                        line.CompanyId = companyId;
                    }

                    db.Expenses.Add(expense);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/expenses/{expense.Id}", expense);
                }, logger, "creating expense"))
            .WithName("CreateExpense");

        // ── Update ────────────────────────────────────────────────────────────
        securedApi.MapPut("/expenses/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Expense expense, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (id != expense.Id) return Results.BadRequest("Expense id mismatch.");
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var existing = await db.Expenses
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (existing is null) return Results.NotFound();

                    existing.CreditorPaydayId    = expense.CreditorPaydayId;
                    existing.CreditorSsn         = expense.CreditorSsn;
                    existing.CreditorName        = expense.CreditorName;
                    existing.PaymentTypePaydayId = expense.PaymentTypePaydayId;
                    existing.PaymentTypeName     = expense.PaymentTypeName;
                    existing.Status              = expense.Status;
                    existing.Date                = expense.Date;
                    existing.DueDate             = expense.DueDate;
                    existing.FinalDueDate        = expense.FinalDueDate;
                    existing.PaidDate            = expense.PaidDate;
                    existing.Reference           = expense.Reference;
                    existing.Deductible          = expense.Deductible;
                    existing.Comments            = expense.Comments;
                    existing.Voucher             = expense.Voucher;
                    existing.AmountExcludingVat  = expense.AmountExcludingVat;
                    existing.AmountIncludingVat  = expense.AmountIncludingVat;
                    existing.AmountVat           = expense.AmountVat;
                    // A change of link is the owner's decision; remember it so
                    // automatic linking neither overrides a choice nor re-links
                    // an expense they took a job off.
                    if (existing.JobId != expense.JobId)
                    {
                        existing.JobLinkSource = expense.JobId is null
                            ? ExpenseJobLinkSource.RemovedByOwner
                            : ExpenseJobLinkSource.Manual;
                    }
                    existing.JobId               = expense.JobId;
                    existing.Source              = expense.Source;
                    existing.PaydayId            = expense.PaydayId;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                }, logger, "updating expense"))
            .WithName("UpdateExpense");

        // ── Delete ────────────────────────────────────────────────────────────
        securedApi.MapDelete("/expenses/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var expense = await db.Expenses
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (expense is null) return Results.NotFound();

                    db.Expenses.Remove(expense);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting expense"))
            .WithName("DeleteExpense");

        // ── Lines ─────────────────────────────────────────────────────────────
        securedApi.MapPost("/expenses/{id:guid}/lines", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, ExpenseLine line, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;
                    if (!await db.Expenses.AnyAsync(x => x.Id == id && x.CompanyId == companyId, ct))
                        return Results.NotFound();

                    line.ExpenseId = id;
                    line.CompanyId = companyId;
                    db.ExpenseLines.Add(line);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/expenses/{id}/lines/{line.Id}", line);
                }, logger, "adding expense line"))
            .WithName("AddExpenseLine");

        securedApi.MapPut("/expenses/{id:guid}/lines/{lineId:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Guid lineId, ExpenseLine line, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (lineId != line.Id) return Results.BadRequest("Line id mismatch.");
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var existing = await db.ExpenseLines
                        .FirstOrDefaultAsync(x => x.Id == lineId && x.ExpenseId == id && x.CompanyId == companyId, ct);
                    if (existing is null) return Results.NotFound();

                    existing.Description           = line.Description;
                    existing.Quantity              = line.Quantity;
                    existing.UnitPriceExcludingVat = line.UnitPriceExcludingVat;
                    existing.UnitPriceIncludingVat = line.UnitPriceIncludingVat;
                    existing.MarkupFactor          = line.MarkupFactor;
                    existing.SalePriceExcludingVat = line.SalePriceExcludingVat;
                    existing.VatPercentage         = line.VatPercentage;
                    existing.DiscountPercentage    = line.DiscountPercentage;
                    existing.AccountId             = line.AccountId;
                    existing.PaydayId              = line.PaydayId;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                }, logger, "updating expense line"))
            .WithName("UpdateExpenseLine");

        securedApi.MapDelete("/expenses/{id:guid}/lines/{lineId:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Guid lineId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var line = await db.ExpenseLines
                        .FirstOrDefaultAsync(x => x.Id == lineId && x.ExpenseId == id && x.CompanyId == companyId, ct);
                    if (line is null) return Results.NotFound();

                    db.ExpenseLines.Remove(line);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting expense line"))
            .WithName("DeleteExpenseLine");
    }
}
