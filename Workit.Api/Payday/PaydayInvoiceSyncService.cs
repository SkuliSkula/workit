using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Payday → Workit for sales invoices. Workit keeps its own copy of every Payday
/// invoice — header, lines and payments — in <c>Invoices</c>, and the invoice
/// routes answer from that copy (<see cref="PaydayInvoiceStore"/>).
/// <see cref="SyncAsync"/> pulls Payday's whole list and upserts by Payday id;
/// <see cref="SaveAsync"/> stores one invoice Payday just handed back (after a
/// create, an edit, or opening it). Called by the sync endpoint, the invoice
/// routes and the background sync. Callers set the company's Payday credentials
/// on the token service first.
/// </summary>
public sealed class PaydayInvoiceSyncService(WorkitDbContext db, IPaydayInvoicesApi payday, ILogger<PaydayInvoiceSyncService> logger)
{
    public sealed record Outcome(PaydayInvoiceSyncResult? Result, string? Error);

    /// <summary>
    /// Payday's reads lag its writes by about a minute, so an invoice stored this
    /// recently may just not be listed yet. It is not removed on that pass.
    /// </summary>
    internal static readonly TimeSpan RemovalGrace = TimeSpan.FromMinutes(10);

    private const string Kind = "invoices";

    public async Task<Outcome> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var listedAt = DateTimeOffset.UtcNow;
        var fetched = new List<PaydayInvoice>();
        var total = 0;
        for (var page = 1; ; page++)
        {
            var result = await payday.GetAllAsync(page: page, perPage: 100, include: "lines,payments");
            if (!result.IsSuccess || result.Value is null)
                return new Outcome(null, result.ErrorMessage ?? "Payday invoices could not be loaded right now.");
            fetched.AddRange(result.Value.Invoices);
            total = result.Value.Total;
            if (page >= result.Value.Pages || result.Value.Invoices.Count == 0) break;
        }

        // Payday's list leaves payments out (52 paid invoices, not one payment, on
        // the first production sync). An open invoice's part-payments are what its
        // balance hangs on, so those few are read one by one, payments included.
        foreach (var open in fetched.Where(i => i.Status == "SENT" && (i.Payments is null || i.Payments.Count == 0)).ToList())
        {
            var detail = await payday.GetByIdAsync(open.Id.ToString(), "lines,payments");
            if (detail is { IsSuccess: true, Value: { } full })
                fetched[fetched.IndexOf(open)] = full;
        }

        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
            return new Outcome(await StoreAllAsync(companyId, fetched, total, listedAt, ct), null);
    }

    private async Task<PaydayInvoiceSyncResult> StoreAllAsync(Guid companyId, List<PaydayInvoice> fetched, int total, DateTimeOffset listedAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        bool SavedSinceListed(Guid id) => PaydaySyncGuard.SavedSince(id, listedAt);
        var stored = await TrackedAsync(companyId, null, ct);
        var byPaydayId = stored.Where(i => i.PaydayId is not null).ToDictionary(i => i.PaydayId!.Value);
        var seen = new HashSet<Guid>();
        int added = 0, updated = 0, removed = 0;

        foreach (var remote in fetched)
        {
            // An invoice created mid-sync shifts the pages; the same one can come twice.
            if (!seen.Add(remote.Id)) continue;
            if (SavedSinceListed(remote.Id)) continue;
            if (byPaydayId.TryGetValue(remote.Id, out var local))
            {
                Apply(local, remote, now);
                updated++;
            }
            else
            {
                Add(companyId, remote, now);
                added++;
            }
        }

        // Payday deletes drafts outright, so "not listed" is the only sign. Trust it
        // only when the list came back whole.
        if (seen.Count >= total)
        {
            foreach (var gone in stored.Where(i =>
                         i.Source == DataSource.Payday && i.PaydayId is Guid id && !seen.Contains(id)
                         && (i.SyncedAt ?? i.CreatedAt) < now - RemovalGrace && !SavedSinceListed(id)))
            {
                db.Invoices.Remove(gone);
                removed++;
            }
        }
        else
        {
            logger.LogWarning("Payday listed {Seen} of {Total} invoices for {Company}; nothing removed this pass", seen.Count, total, companyId);
        }

        await db.SaveChangesAsync(ct);
        return new PaydayInvoiceSyncResult(seen.Count, added, updated, removed, now);
    }

    /// <summary>
    /// Stores one invoice exactly as Payday returned it. Lines and payments are
    /// replaced only when Payday included them; a header-only response leaves
    /// the stored ones as they are.
    /// </summary>
    public async Task SaveAsync(Guid companyId, PaydayInvoice remote, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var now = DateTimeOffset.UtcNow;
            var local = (await TrackedAsync(companyId, remote.Id, ct)).FirstOrDefault();
            if (local is null) Add(companyId, remote, now);
            else Apply(local, remote, now);
            await db.SaveChangesAsync(ct);
            PaydaySyncGuard.SavedDirectly(remote.Id);
        }
    }

    /// <summary>Drops Workit's copy of an invoice Payday no longer has.</summary>
    public async Task RemoveAsync(Guid companyId, Guid paydayId, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            foreach (var local in await TrackedAsync(companyId, paydayId, ct))
                db.Invoices.Remove(local);
            await db.SaveChangesAsync(ct);
            // A list fetched just now may still carry it; don't bring it back.
            PaydaySyncGuard.SavedDirectly(paydayId);
        }
    }

    private Task<List<Invoice>> TrackedAsync(Guid companyId, Guid? paydayId, CancellationToken ct)
    {
        var query = db.Invoices.Include(i => i.Lines).Include(i => i.Payments).Where(i => i.CompanyId == companyId);
        if (paydayId is Guid id) query = query.Where(i => i.PaydayId == id);
        return query.ToListAsync(ct);
    }

    private void Add(Guid companyId, PaydayInvoice remote, DateTimeOffset now)
    {
        var local = new Invoice
        {
            CompanyId = companyId,
            PaydayId  = remote.Id,
            Source    = DataSource.Payday,
            CreatedAt = remote.Created is DateTime created ? new DateTimeOffset(PaydaySyncGuard.Utc(created)) : now,
        };
        db.Invoices.Add(local);
        Apply(local, remote, now);
    }

    private void Apply(Invoice local, PaydayInvoice remote, DateTimeOffset now)
    {
        // Edit responses can leave the customer out; keep the one already stored.
        if (remote.Customer is { } customer)
        {
            local.CustomerPaydayId = customer.Id;
            local.CustomerSsn      = customer.Ssn;
            local.CustomerName     = customer.Name;
        }
        if (remote.Payor is { } payor)
        {
            local.PayorPaydayId = payor.Id;
            local.PayorSsn      = payor.Ssn;
            local.PayorName     = payor.Name;
        }

        local.Number      = remote.Number ?? local.Number;
        if (!string.IsNullOrWhiteSpace(remote.Status)) local.Status = remote.Status;
        local.Description = remote.Description;
        local.Reference   = remote.Reference;
        if (!string.IsNullOrWhiteSpace(remote.CurrencyCode)) local.CurrencyCode = remote.CurrencyCode;
        if (remote.CurrencyRate > 0) local.CurrencyRate = remote.CurrencyRate;

        local.InvoiceDate        = PaydaySyncGuard.Utc(remote.InvoiceDate);
        local.DueDate            = PaydaySyncGuard.Utc(remote.DueDate);
        local.FinalDueDate       = PaydaySyncGuard.Utc(remote.FinalDueDate);
        local.SentDate           = PaydaySyncGuard.Utc(remote.SentDate);
        local.PaidDate           = PaydaySyncGuard.Utc(remote.PaidDate);
        local.CancelDate         = PaydaySyncGuard.Utc(remote.CancelDate);
        local.CancelledDate      = PaydaySyncGuard.Utc(remote.CancelledDate);
        local.CreditDate         = PaydaySyncGuard.Utc(remote.CreditDate);
        local.RefundDate         = PaydaySyncGuard.Utc(remote.RefundDate);
        local.ClaimCreated       = PaydaySyncGuard.Utc(remote.ClaimCreated);
        local.ClaimFinalDueDate  = PaydaySyncGuard.Utc(remote.ClaimFinalDueDate);
        local.ClaimCancelledDate = PaydaySyncGuard.Utc(remote.ClaimCancelledDate);
        local.ClaimCancelled     = remote.ClaimCancelled;

        local.AmountExcludingVat        = remote.AmountExcludingVat;
        local.AmountIncludingVat        = remote.AmountIncludingVat;
        local.AmountVat                 = remote.AmountVat;
        local.ForeignAmountExcludingVat = remote.ForeignAmountExcludingVat;
        local.ForeignAmountIncludingVat = remote.ForeignAmountIncludingVat;
        local.ForeignAmountVat          = remote.ForeignAmountVat;

        local.VatNumber                = remote.VatNumber;
        local.CreateClaim              = remote.CreateClaim;
        local.CreateElectronicInvoice  = remote.CreateElectronicInvoice;
        local.ElectronicInvoicePartyId = remote.ElectronicInvoicePartyId;
        local.SendEmail                = remote.SendEmail;
        local.DefaultInterest          = remote.DefaultInterest;
        local.CapitalGainsTax          = remote.CapitalGainsTax;
        local.AccountingCost           = remote.AccountingCost;
        local.Ocr                      = remote.Ocr;
        local.Source                   = DataSource.Payday;
        local.SyncedAt                 = now;

        if (remote.Lines is not null) ApplyLines(local, remote.Lines);
        if (remote.Payments is not null) ApplyPayments(local, remote.Payments);
    }

    private void ApplyLines(Invoice local, List<PaydayInvoiceLine> remote)
    {
        var byPaydayId = local.Lines.Where(l => l.PaydayId is not null).ToDictionary(l => l.PaydayId!.Value);
        var keep = new HashSet<Guid>();
        foreach (var r in remote)
        {
            if (!byPaydayId.TryGetValue(r.Id, out var line))
            {
                line = new InvoiceLine { InvoiceId = local.Id, CompanyId = local.CompanyId, PaydayId = r.Id };
                local.Lines.Add(line);
                db.InvoiceLines.Add(line);
            }
            keep.Add(line.Id);
            line.Position                     = r.Position;
            line.Description                  = r.Description;
            line.Comment                      = r.Comment;
            line.Quantity                     = r.Quantity ?? 0m;
            line.UnitPriceExcludingVat        = r.UnitPriceExcludingVat ?? 0m;
            line.UnitPriceIncludingVat        = r.UnitPriceIncludingVat ?? 0m;
            line.ForeignUnitPriceExcludingVat = r.ForeignUnitPriceExcludingVat;
            line.ForeignUnitPriceIncludingVat = r.ForeignUnitPriceIncludingVat;
            line.VatPercentage                = r.VatPercentage ?? 0m;
            line.DiscountPercentage           = r.DiscountPercentage;
            line.Sku                          = r.Sku;
            line.ProductId                    = r.ProductId;
        }
        foreach (var stale in local.Lines.Where(l => !keep.Contains(l.Id)).ToList())
        {
            local.Lines.Remove(stale);
            db.InvoiceLines.Remove(stale);
        }
    }

    private void ApplyPayments(Invoice local, List<PaydayInvoicePayment> remote)
    {
        var byPaydayId = local.Payments.Where(p => p.PaydayId is not null).ToDictionary(p => p.PaydayId!.Value);
        var keep = new HashSet<Guid>();
        foreach (var r in remote)
        {
            if (!byPaydayId.TryGetValue(r.Id, out var payment))
            {
                payment = new InvoicePayment { InvoiceId = local.Id, CompanyId = local.CompanyId, PaydayId = r.Id };
                local.Payments.Add(payment);
                db.InvoicePayments.Add(payment);
            }
            keep.Add(payment.Id);
            payment.PaymentTypePaydayId = r.PaymentTypeId == Guid.Empty ? null : r.PaymentTypeId;
            payment.PaymentDate         = PaydaySyncGuard.Utc(r.PaymentDate);
            payment.Amount              = r.Amount;
            payment.CurrencyCode        = r.CurrencyCode;
        }
        foreach (var stale in local.Payments.Where(p => !keep.Contains(p.Id)).ToList())
        {
            local.Payments.Remove(stale);
            db.InvoicePayments.Remove(stale);
        }
    }
}
