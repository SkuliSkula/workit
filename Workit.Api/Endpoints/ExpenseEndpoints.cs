using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
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

                    return Results.Ok(expense);
                }, logger, "loading expense"))
            .WithName("GetExpense");

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
