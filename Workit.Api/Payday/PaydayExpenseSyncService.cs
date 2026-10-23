using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Payday → Workit for purchase expenses. Workit keeps its own copy of every
/// Payday expense and its lines in <c>Expenses</c>, and the expense routes answer
/// from that copy (<see cref="PaydayExpenseStore"/>).
/// <para>
/// Payday owns the purchase itself — creditor, dates, status, amounts, and each
/// line's description, quantity, list price, VAT and vendor discount. Workit owns
/// what it adds: the job link, the sale price (set by <see cref="ExpensePricing"/>,
/// or an owner's own), and the billings of each line. A sync refreshes Payday's part and never touches
/// Workit's: an owner's sale price stays, a billed line is never removed, and an
/// expense something was billed from is kept (marked DELETED) if Payday drops it.
/// </para>
/// Callers set the company's Payday credentials on the token service first.
/// </summary>
public sealed class PaydayExpenseSyncService(WorkitDbContext db, IPaydayExpensesApi payday, ILogger<PaydayExpenseSyncService> logger)
{
    public sealed record Outcome(PaydayExpenseSyncResult? Result, string? Error);

    /// <summary>An expense stored this recently may just not be listed yet; it is not removed on that pass.</summary>
    internal static readonly TimeSpan RemovalGrace = TimeSpan.FromMinutes(10);

    /// <summary>Status given to a billed expense Payday no longer has, so its history stays.</summary>
    public const string DeletedStatus = "DELETED";

    private const string Kind = "expenses";

    public async Task<Outcome> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var listedAt = DateTimeOffset.UtcNow;
        var fetched = new List<PaydayExpense>();
        var total = 0;
        for (var page = 1; ; page++)
        {
            var result = await payday.GetAllAsync(page: page, perPage: 100, include: "lines");
            if (!result.IsSuccess || result.Value is null)
                return new Outcome(null, result.ErrorMessage ?? "Payday expenses could not be loaded right now.");
            fetched.AddRange(result.Value.Expenses);
            total = result.Value.Total;
            if (page >= result.Value.Pages || result.Value.Expenses.Count == 0) break;
        }

        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
            return new Outcome(await StoreAllAsync(companyId, fetched, total, listedAt, ct), null);
    }

    private async Task<PaydayExpenseSyncResult> StoreAllAsync(Guid companyId, List<PaydayExpense> fetched, int total, DateTimeOffset listedAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var stored = await TrackedAsync(companyId, null, ct);
        var billed = await BilledLineIdsAsync(companyId, ct);
        var byPaydayId = stored.Where(e => e.PaydayId is not null).ToDictionary(e => e.PaydayId!.Value);
        var seen = new HashSet<Guid>();
        int added = 0, updated = 0, removed = 0;

        foreach (var remote in fetched)
        {
            // An expense created mid-sync shifts the pages; the same one can come twice.
            if (!seen.Add(remote.Id)) continue;
            if (PaydaySyncGuard.SavedSince(remote.Id, listedAt)) continue;
            if (byPaydayId.TryGetValue(remote.Id, out var local))
            {
                Apply(local, remote, billed, now);
                updated++;
            }
            else
            {
                Add(companyId, remote, now);
                added++;
            }
        }

        // Payday deletes expenses outright, so "not listed" is the only sign. Trust
        // it only when the list came back whole.
        if (seen.Count >= total)
        {
            foreach (var gone in stored.Where(e =>
                         e.Source == DataSource.Payday && e.PaydayId is Guid id && !seen.Contains(id)
                         && e.Status != DeletedStatus
                         && (e.SyncedAt ?? e.CreatedAt) < now - RemovalGrace
                         && !PaydaySyncGuard.SavedSince(id, listedAt)))
            {
                Drop(gone, billed, now);
                removed++;
            }
        }
        else
        {
            logger.LogWarning("Payday listed {Seen} of {Total} expenses for {Company}; nothing removed this pass", seen.Count, total, companyId);
        }

        await db.SaveChangesAsync(ct);
        await ExpensePricing.RepriceAsync(db, companyId, ct);
        return new PaydayExpenseSyncResult(seen.Count, added, updated, removed, now);
    }

    /// <summary>
    /// Stores one expense as Payday returned it. Lines are refreshed only when
    /// Payday included them; a header-only response leaves them as they are.
    /// </summary>
    public async Task SaveAsync(Guid companyId, PaydayExpense remote, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var now = DateTimeOffset.UtcNow;
            var local = (await TrackedAsync(companyId, remote.Id, ct)).FirstOrDefault();
            if (local is null) Add(companyId, remote, now);
            else Apply(local, remote, await BilledLineIdsAsync(companyId, ct), now);
            await db.SaveChangesAsync(ct);
            await ExpensePricing.RepriceAsync(db, companyId, ct);
            PaydaySyncGuard.SavedDirectly(remote.Id);
        }
    }

    /// <summary>Drops Workit's copy of an expense Payday no longer has — or keeps it as DELETED if anything was billed from it.</summary>
    public async Task RemoveAsync(Guid companyId, Guid paydayId, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var billed = await BilledLineIdsAsync(companyId, ct);
            foreach (var local in await TrackedAsync(companyId, paydayId, ct))
                Drop(local, billed, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            // A list fetched just now may still carry it; don't bring it back.
            PaydaySyncGuard.SavedDirectly(paydayId);
        }
    }

    private void Drop(Expense local, HashSet<Guid> billed, DateTimeOffset now)
    {
        if (local.Lines.Any(l => billed.Contains(l.Id)))
        {
            // Hours of invoicing point at these lines; keep the record, say what happened.
            local.Status   = DeletedStatus;
            local.SyncedAt = now;
        }
        else
        {
            db.Expenses.Remove(local);
        }
    }

    private Task<List<Expense>> TrackedAsync(Guid companyId, Guid? paydayId, CancellationToken ct)
    {
        var query = db.Expenses.Include(e => e.Lines).Where(e => e.CompanyId == companyId);
        if (paydayId is Guid id) query = query.Where(e => e.PaydayId == id);
        return query.ToListAsync(ct);
    }

    private async Task<HashSet<Guid>> BilledLineIdsAsync(Guid companyId, CancellationToken ct) =>
        (await db.ExpenseLineBillings.Where(b => b.CompanyId == companyId).Select(b => b.ExpenseLineId).Distinct().ToListAsync(ct))
        .ToHashSet();

    private void Add(Guid companyId, PaydayExpense remote, DateTimeOffset now)
    {
        // The mapper prices each line: what was paid (list less the vendor's discount), marked up.
        var local = remote.ToWorkitExpense(jobId: null, companyId);
        local.JobLinkSource = ExpenseJobLinkSource.None;
        local.CreatedAt     = remote.Created is DateTime created ? new DateTimeOffset(PaydaySyncGuard.Utc(created)) : now;
        ApplyHeader(local, remote, now);
        foreach (var line in local.Lines)
        {
            line.ExpenseId = local.Id;
            line.CompanyId = companyId;
        }
        db.Expenses.Add(local);
    }

    private void Apply(Expense local, PaydayExpense remote, HashSet<Guid> billed, DateTimeOffset now)
    {
        ApplyHeader(local, remote, now);
        if (remote.Lines is not null) ApplyLines(local, remote.Lines, billed);
    }

    // Payday's half of the expense. JobId and JobLinkSource are Workit's and stay.
    private static void ApplyHeader(Expense local, PaydayExpense remote, DateTimeOffset now)
    {
        if (remote.Creditor is { } creditor)
        {
            local.CreditorPaydayId = creditor.Id;
            local.CreditorSsn      = creditor.Ssn;
            local.CreditorName     = creditor.Name;
        }
        if (remote.PaymentType is { } paymentType)
        {
            local.PaymentTypePaydayId = paymentType.Id;
            local.PaymentTypeName     = paymentType.Name;
        }
        if (!string.IsNullOrWhiteSpace(remote.Status)) local.Status = remote.Status;
        local.Date               = PaydaySyncGuard.Utc(remote.Date);
        local.DueDate            = PaydaySyncGuard.Utc(remote.DueDate);
        local.FinalDueDate       = PaydaySyncGuard.Utc(remote.FinalDueDate);
        local.PaidDate           = PaydaySyncGuard.Utc(remote.PaidDate);
        local.Reference          = remote.Reference;
        local.Deductible         = remote.Deductible;
        local.Comments           = remote.Comments;
        local.Voucher            = remote.Voucher;
        local.AmountExcludingVat = remote.AmountExcludingVat;
        local.AmountIncludingVat = remote.AmountIncludingVat;
        local.AmountVat          = remote.AmountVat;
        local.Source             = DataSource.Payday;
        local.SyncedAt           = now;
    }

    private void ApplyLines(Expense local, List<PaydayExpenseLine> remote, HashSet<Guid> billed)
    {
        var byPaydayId = local.Lines.Where(l => l.PaydayId is not null).ToDictionary(l => l.PaydayId!.Value);
        var keep = new HashSet<Guid>();
        foreach (var r in remote)
        {
            if (!byPaydayId.TryGetValue(r.Id, out var line))
            {
                line = new ExpenseLine
                {
                    ExpenseId    = local.Id,
                    CompanyId    = local.CompanyId,
                    PaydayId     = r.Id,
                    MarkupFactor = 1m,
                };
                local.Lines.Add(line);
                db.ExpenseLines.Add(line);
            }
            // The sale price is ExpensePricing's, set once the lines are saved.
            keep.Add(line.Id);
            line.Description           = r.Description;
            line.Quantity              = r.Quantity;
            line.UnitPriceExcludingVat = r.UnitPriceExcludingVat;
            line.UnitPriceIncludingVat = r.UnitPriceIncludingVat;
            line.VatPercentage         = r.VatPercentage;
            line.DiscountPercentage    = r.DiscountPercentage;
            line.AccountId             = r.AccountId;
        }
        // Lines Payday dropped go too — unless something was billed from them, or
        // they were added in Workit (no Payday id).
        foreach (var stale in local.Lines.Where(l => l.PaydayId is not null && !keep.Contains(l.Id) && !billed.Contains(l.Id)).ToList())
        {
            local.Lines.Remove(stale);
            db.ExpenseLines.Remove(stale);
        }
    }
}
