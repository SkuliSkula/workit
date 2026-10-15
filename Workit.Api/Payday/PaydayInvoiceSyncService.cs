using System.Collections.Concurrent;
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

    // The background sync, a page's refresh and an invoice write can land at the
    // same moment; one at a time per company, so the same invoice is never added twice.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();
    private static SemaphoreSlim Gate(Guid companyId) => Gates.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Invoices stored straight from a Payday write or open, and when. A list
    /// fetched within <see cref="ListLag"/> of that can still show the invoice as
    /// it was before, so the sync leaves those alone; the next pass catches up.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> DirectSaves = new();
    internal static readonly TimeSpan ListLag = TimeSpan.FromMinutes(2);

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

        var gate = Gate(companyId);
        await gate.WaitAsync(ct);
        try
        {
            return new Outcome(await StoreAllAsync(companyId, fetched, total, listedAt, ct), null);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PaydayInvoiceSyncResult> StoreAllAsync(Guid companyId, List<PaydayInvoice> fetched, int total, DateTimeOffset listedAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var old in DirectSaves.Where(d => d.Value < now - ListLag).ToList())
            DirectSaves.TryRemove(old);
        bool SavedSinceListed(Guid id) => DirectSaves.TryGetValue(id, out var at) && at > listedAt - ListLag;
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
        var gate = Gate(companyId);
        await gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var local = (await TrackedAsync(companyId, remote.Id, ct)).FirstOrDefault();
            if (local is null) Add(companyId, remote, now);
            else Apply(local, remote, now);
            await db.SaveChangesAsync(ct);
            DirectSaves[remote.Id] = now;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops Workit's copy of an invoice Payday no longer has.</summary>
    public async Task RemoveAsync(Guid companyId, Guid paydayId, CancellationToken ct)
    {
        var gate = Gate(companyId);
        await gate.WaitAsync(ct);
        try
        {
            foreach (var local in await TrackedAsync(companyId, paydayId, ct))
                db.Invoices.Remove(local);
            await db.SaveChangesAsync(ct);
            // A list fetched just now may still carry it; don't bring it back.
            DirectSaves[paydayId] = DateTimeOffset.UtcNow;
        }
        finally
        {
            gate.Release();
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
            CreatedAt = remote.Created is DateTime created ? new DateTimeOffset(Utc(created)) : now,
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

        local.InvoiceDate        = Utc(remote.InvoiceDate);
        local.DueDate            = Utc(remote.DueDate);
        local.FinalDueDate       = Utc(remote.FinalDueDate);
        local.SentDate           = Utc(remote.SentDate);
        local.PaidDate           = Utc(remote.PaidDate);
        local.CancelDate         = Utc(remote.CancelDate);
        local.CancelledDate      = Utc(remote.CancelledDate);
        local.CreditDate         = Utc(remote.CreditDate);
        local.RefundDate         = Utc(remote.RefundDate);
        local.ClaimCreated       = Utc(remote.ClaimCreated);
        local.ClaimFinalDueDate  = Utc(remote.ClaimFinalDueDate);
        local.ClaimCancelledDate = Utc(remote.ClaimCancelledDate);
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
            payment.PaymentDate         = Utc(r.PaymentDate);
            payment.Amount              = r.Amount;
            payment.CurrencyCode        = r.CurrencyCode;
        }
        foreach (var stale in local.Payments.Where(p => !keep.Contains(p.Id)).ToList())
        {
            local.Payments.Remove(stale);
            db.InvoicePayments.Remove(stale);
        }
    }

    // Payday sends plain dates ("2026-09-21T00:00:00"); Iceland is UTC all year,
    // and Postgres timestamptz needs the kind spelled out.
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static DateTime? Utc(DateTime? value) => value is DateTime v ? Utc(v) : null;
}
