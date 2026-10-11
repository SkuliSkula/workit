using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// What a Payday invoice holds in Workit — the hours, materials and expense
/// quantities put on it — and how they come back when the invoice goes away.
///
/// The builder marks everything invoiced the moment it creates a draft, so a
/// draft that is deleted or cancelled has to hand its records back; otherwise
/// they sit marked invoiced with nothing billing them and never reappear in the
/// builder. Records are tied to the invoice by its Payday id: a draft has no
/// number until it is issued, so the number alone pointed at nothing.
/// </summary>
internal static class InvoiceRelease
{
    /// <summary>Statuses under which an invoice bills nothing, so what it held is free again.</summary>
    internal static bool ReleasesRecords(string? status) =>
        string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
     || string.Equals(status, "DELETED", StringComparison.OrdinalIgnoreCase);

    internal sealed record Released(int TimeEntries, int MaterialUsages, int ExpenseBillings)
    {
        public int Total => TimeEntries + MaterialUsages + ExpenseBillings;
    }

    /// <summary>
    /// Hours and materials go back to not invoiced; expense-line billings are
    /// removed, which puts their quantity back on the line to bill again.
    /// Stock is not touched: a draft never consumed any, and Payday reverses a
    /// cancelled invoice's stock itself.
    /// </summary>
    internal static async Task<Released> ReleaseAsync(WorkitDbContext db, Guid companyId, Guid paydayInvoiceId, CancellationToken ct)
    {
        var entries = await db.TimeEntries
            .Where(e => e.CompanyId == companyId && e.PaydayInvoiceId == paydayInvoiceId)
            .ToListAsync(ct);
        foreach (var e in entries)
        {
            e.IsInvoiced          = false;
            e.InvoicedAt          = null;
            e.PaydayInvoiceNumber = null;
            e.PaydayInvoiceId     = null;
        }

        var usages = await db.MaterialUsages
            .Where(u => u.CompanyId == companyId && u.PaydayInvoiceId == paydayInvoiceId)
            .ToListAsync(ct);
        foreach (var u in usages)
        {
            u.IsInvoiced          = false;
            u.InvoicedAt          = null;
            u.PaydayInvoiceNumber = null;
            u.PaydayInvoiceId     = null;
        }

        var billings = await db.ExpenseLineBillings
            .Where(b => b.CompanyId == companyId && b.PaydayInvoiceId == paydayInvoiceId)
            .ToListAsync(ct);
        db.ExpenseLineBillings.RemoveRange(billings);

        await db.SaveChangesAsync(ct);
        return new Released(entries.Count, usages.Count, billings.Count);
    }

    /// <summary>
    /// Once a draft is issued it gets its number; copy it onto the records the
    /// draft holds, so the invoice badge and "reset invoice #N" find them.
    /// </summary>
    internal static async Task<int> BackfillNumberAsync(WorkitDbContext db, Guid companyId, Guid paydayInvoiceId, int number, CancellationToken ct)
    {
        var changed = 0;
        foreach (var e in await db.TimeEntries
                     .Where(x => x.CompanyId == companyId && x.PaydayInvoiceId == paydayInvoiceId && x.PaydayInvoiceNumber == null)
                     .ToListAsync(ct))
        { e.PaydayInvoiceNumber = number; changed++; }

        foreach (var u in await db.MaterialUsages
                     .Where(x => x.CompanyId == companyId && x.PaydayInvoiceId == paydayInvoiceId && x.PaydayInvoiceNumber == null)
                     .ToListAsync(ct))
        { u.PaydayInvoiceNumber = number; changed++; }

        foreach (var b in await db.ExpenseLineBillings
                     .Where(x => x.CompanyId == companyId && x.PaydayInvoiceId == paydayInvoiceId && x.PaydayInvoiceNumber == null)
                     .ToListAsync(ct))
        { b.PaydayInvoiceNumber = number; changed++; }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Every Payday invoice id that still holds records in this company.</summary>
    internal static async Task<List<Guid>> HeldInvoiceIdsAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var fromEntries  = db.TimeEntries.Where(e => e.CompanyId == companyId && e.PaydayInvoiceId != null).Select(e => e.PaydayInvoiceId!.Value);
        var fromUsages   = db.MaterialUsages.Where(u => u.CompanyId == companyId && u.PaydayInvoiceId != null).Select(u => u.PaydayInvoiceId!.Value);
        var fromBillings = db.ExpenseLineBillings.Where(b => b.CompanyId == companyId && b.PaydayInvoiceId != null).Select(b => b.PaydayInvoiceId!.Value);

        return (await fromEntries.ToListAsync(ct))
            .Concat(await fromUsages.ToListAsync(ct))
            .Concat(await fromBillings.ToListAsync(ct))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Catches drafts deleted or cancelled inside Payday, where Workit never saw
    /// it happen. Each invoice is asked for by id, and its records are released
    /// only when Payday says plainly that it is gone (404) or no longer bills
    /// (cancelled/deleted). A timeout or an error releases nothing — releasing on
    /// a guess could free hours that sit on a live draft and bill them twice.
    /// </summary>
    internal static async Task<InvoiceReconcileResult> ReconcileAsync(
        WorkitDbContext db, IPaydayInvoicesApi payday, Guid companyId, CancellationToken ct)
    {
        var ids = await HeldInvoiceIdsAsync(db, companyId, ct);
        int released = 0, records = 0, numbers = 0, unreachable = 0;

        foreach (var id in ids)
        {
            var result = await payday.GetByIdAsync(id.ToString(), include: null);

            if (result.IsNotFound || (result.IsSuccess && ReleasesRecords(result.Value?.Status)))
            {
                var r = await ReleaseAsync(db, companyId, id, ct);
                released++;
                records += r.Total;
            }
            else if (result.IsSuccess && result.Value?.Number is int number)
            {
                numbers += await BackfillNumberAsync(db, companyId, id, number, ct);
            }
            else if (!result.IsSuccess)
            {
                unreachable++;   // could not tell — leave it exactly as it is
            }
        }

        return new InvoiceReconcileResult(ids.Count, released, records, numbers, unreachable);
    }
}
