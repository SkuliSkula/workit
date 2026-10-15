using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Reads Workit's stored copy of the company's Payday expenses back out in
/// Payday's own shapes, with Payday's list filters, so the Expenses page keeps
/// working unchanged while reading from Workit. Filled by
/// <see cref="PaydayExpenseSyncService"/>.
/// </summary>
internal static class PaydayExpenseStore
{
    public sealed record ListQuery(
        int     Page     = 1,
        int     PerPage  = 25,
        string? Include  = "lines",
        string? DateFrom = null,
        string? DateTo   = null,
        string? Status   = null,
        string  Order    = "desc",
        string  OrderBy  = "date");

    public static async Task<PaydayExpensesResponse> ListAsync(WorkitDbContext db, Guid companyId, ListQuery q, CancellationToken ct)
    {
        var query = Filtered(db, companyId, q.DateFrom, q.DateTo, q.Status);
        var total = await query.CountAsync(ct);

        var perPage = Math.Clamp(q.PerPage, 1, 500);
        var page    = Math.Max(1, q.Page);
        var rows = await Ordered(query, q.Order, q.OrderBy)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);
        await AttachLinesAsync(db, rows, q.Include, ct);

        return new PaydayExpensesResponse
        {
            Expenses = rows.Select(e => ToPayday(e, q.Include)).ToList(),
            PerPage  = perPage,
            Page     = page,
            Pages    = Math.Max(1, (int)Math.Ceiling(total / (double)perPage)),
            Total    = total,
        };
    }

    /// <summary>Every stored expense in the window, lines included — what job-code matching reads.</summary>
    public static async Task<List<PaydayExpense>> AllAsync(WorkitDbContext db, Guid companyId, string? dateFrom, string? dateTo, CancellationToken ct)
    {
        var rows = await Filtered(db, companyId, dateFrom, dateTo, null).ToListAsync(ct);
        await AttachLinesAsync(db, rows, "lines", ct);
        return rows.Select(e => ToPayday(e, "lines")).ToList();
    }

    public static async Task<PaydayExpense?> GetAsync(WorkitDbContext db, Guid companyId, Guid paydayId, string? include, CancellationToken ct)
    {
        var expense = await db.Expenses.AsNoTracking()
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.PaydayId == paydayId, ct);
        if (expense is null) return null;
        await AttachLinesAsync(db, [expense], include, ct);
        return ToPayday(expense, include);
    }

    public static async Task<PaydayExpenseSyncStatus> StatusAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var stored = db.Expenses.Where(e => e.CompanyId == companyId && e.PaydayId != null && e.Status != PaydayExpenseSyncService.DeletedStatus);
        return new PaydayExpenseSyncStatus(
            await stored.CountAsync(ct),
            await db.Expenses.Where(e => e.CompanyId == companyId && e.PaydayId != null).MaxAsync(e => e.SyncedAt, ct));
    }

    private static IQueryable<Expense> Filtered(WorkitDbContext db, Guid companyId, string? dateFrom, string? dateTo, string? status)
    {
        var query = db.Expenses.AsNoTracking().Where(e => e.CompanyId == companyId && e.PaydayId != null);
        if (ParseDate(dateFrom) is DateTime from)
            query = query.Where(e => e.Date >= from);
        if (ParseDate(dateTo) is DateTime to)
        {
            var end = to.AddDays(1);
            query = query.Where(e => e.Date < end);
        }
        var statuses = (status ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToList();
        // Payday has no deleted expenses to list; a billed one Workit kept as DELETED
        // shows only when asked for.
        query = statuses.Count > 0
            ? query.Where(e => statuses.Contains(e.Status))
            : query.Where(e => e.Status != PaydayExpenseSyncService.DeletedStatus);
        return query;
    }

    private static IQueryable<Expense> Ordered(IQueryable<Expense> query, string order, string orderBy)
    {
        var desc = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);
        return (orderBy ?? "").ToLowerInvariant() switch
        {
            "duedate" => desc
                ? query.OrderByDescending(e => e.DueDate).ThenByDescending(e => e.CreatedAt)
                : query.OrderBy(e => e.DueDate).ThenBy(e => e.CreatedAt),
            "amount" => desc
                ? query.OrderByDescending(e => e.AmountIncludingVat).ThenByDescending(e => e.CreatedAt)
                : query.OrderBy(e => e.AmountIncludingVat).ThenBy(e => e.CreatedAt),
            _ => desc
                ? query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
                : query.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt),
        };
    }

    private static async Task AttachLinesAsync(WorkitDbContext db, List<Expense> rows, string? include, CancellationToken ct)
    {
        if (!Includes(include, "lines")) return;
        var ids = rows.Select(e => e.Id).ToList();
        var lines = (await db.ExpenseLines.AsNoTracking().Where(l => ids.Contains(l.ExpenseId)).ToListAsync(ct)).ToLookup(l => l.ExpenseId);
        foreach (var row in rows) row.Lines = lines[row.Id].ToList();
    }

    private static PaydayExpense ToPayday(Expense e, string? include) => new()
    {
        Id          = e.PaydayId!.Value,
        Status      = e.Status,
        Creditor    = new PaydayExpenseCreditor { Id = e.CreditorPaydayId, Ssn = e.CreditorSsn, Name = e.CreditorName },
        PaymentType = e.PaymentTypePaydayId is null && e.PaymentTypeName is null
            ? null
            : new PaydayPaymentTypeRef { Id = e.PaymentTypePaydayId, Name = e.PaymentTypeName },
        PaidDate           = e.PaidDate,
        Date               = e.Date,
        DueDate            = e.DueDate,
        FinalDueDate       = e.FinalDueDate,
        Reference          = e.Reference,
        Deductible         = e.Deductible,
        Comments           = e.Comments,
        Voucher            = e.Voucher,
        AmountExcludingVat = e.AmountExcludingVat,
        AmountIncludingVat = e.AmountIncludingVat,
        AmountVat          = e.AmountVat,
        Created            = e.CreatedAt.UtcDateTime,
        Lines = Includes(include, "lines")
            ? e.Lines.Select(l => new PaydayExpenseLine
            {
                Id                    = l.PaydayId ?? l.Id,
                ExpenseId             = e.PaydayId!.Value,
                Description           = l.Description,
                Quantity              = l.Quantity,
                UnitPriceExcludingVat = l.UnitPriceExcludingVat,
                UnitPriceIncludingVat = l.UnitPriceIncludingVat,
                VatPercentage         = l.VatPercentage,
                DiscountPercentage    = l.DiscountPercentage,
                AccountId             = l.AccountId,
            }).ToList()
            : null,
    };

    private static bool Includes(string? include, string part) =>
        (include ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(part, StringComparer.OrdinalIgnoreCase);

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
}
