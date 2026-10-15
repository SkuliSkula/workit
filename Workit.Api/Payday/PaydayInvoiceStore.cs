using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Reads Workit's stored copy of the company's Payday invoices back out in
/// Payday's own shapes, with Payday's list filters, so the console pages that
/// were built against Payday keep working unchanged while reading from Workit.
/// Filled by <see cref="PaydayInvoiceSyncService"/>.
/// </summary>
internal static class PaydayInvoiceStore
{
    public sealed record ListQuery(
        int     Page          = 1,
        int     PerPage       = 25,
        string? Include       = "lines",
        string? DateFrom      = null,
        string? DateTo        = null,
        string? ExcludeStatus = null,
        Guid?   CustomerId    = null,
        string? Query         = null,
        string  Order         = "desc",
        string  OrderBy       = "number");

    public static async Task<PaydayInvoicesResponse> ListAsync(WorkitDbContext db, Guid companyId, ListQuery q, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.PaydayId != null);

        if (ParseDate(q.DateFrom) is DateTime from)
            query = query.Where(i => i.InvoiceDate >= from);
        if (ParseDate(q.DateTo) is DateTime to)
        {
            var end = to.AddDays(1);
            query = query.Where(i => i.InvoiceDate < end);
        }
        var excluded = (q.ExcludeStatus ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToList();
        if (excluded.Count > 0)
            query = query.Where(i => !excluded.Contains(i.Status));
        if (q.CustomerId is Guid customerId)
            query = query.Where(i => i.CustomerPaydayId == customerId);
        if (!string.IsNullOrWhiteSpace(q.Query))
        {
            var term = q.Query.Trim().ToLower();
            query = query.Where(i =>
                (i.CustomerName != null && i.CustomerName.ToLower().Contains(term)) ||
                (i.Description  != null && i.Description.ToLower().Contains(term))  ||
                (i.Reference    != null && i.Reference.ToLower().Contains(term))    ||
                (i.CustomerSsn  != null && i.CustomerSsn.Contains(term))            ||
                (i.Number != null && i.Number.ToString()!.Contains(term)));
        }

        var totals = await query
            .Select(i => new { i.Status, i.AmountIncludingVat, Paid = i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m })
            .ToListAsync(ct);

        var perPage = Math.Clamp(q.PerPage, 1, 500);
        var pages   = Math.Max(1, (int)Math.Ceiling(totals.Count / (double)perPage));
        var page    = Math.Max(1, q.Page);

        var rows = await Ordered(query, q.Order, q.OrderBy)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);
        await AttachAsync(db, rows, q.Include, ct);
        var emails = await EmailsAsync(db, companyId, rows, ct);

        return new PaydayInvoicesResponse
        {
            Invoices = rows.Select(i => ToPayday(i, q.Include, emails)).ToList(),
            PerPage  = perPage,
            Page     = page,
            Pages    = pages,
            Total    = totals.Count,
            Summary  = new PaydayInvoicesSummary
            {
                TotalCount   = totals.Count,
                PaidAmount   = totals.Sum(t => t.Status == "PAID" ? t.AmountIncludingVat : t.Status == "SENT" ? t.Paid : 0m),
                UnpaidAmount = totals.Sum(t => t.Status == "SENT" ? Math.Max(0m, t.AmountIncludingVat - t.Paid) : 0m),
            },
        };
    }

    public static async Task<PaydayInvoice?> GetAsync(WorkitDbContext db, Guid companyId, Guid paydayId, string? include, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.PaydayId == paydayId, ct);
        if (invoice is null) return null;
        var rows = new List<Invoice> { invoice };
        await AttachAsync(db, rows, include, ct);
        return ToPayday(invoice, include, await EmailsAsync(db, companyId, rows, ct));
    }

    public static async Task<PaydayInvoiceSyncStatus> StatusAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var stored = db.Invoices.Where(i => i.CompanyId == companyId && i.PaydayId != null);
        return new PaydayInvoiceSyncStatus(
            await stored.CountAsync(ct),
            await stored.MaxAsync(i => i.SyncedAt, ct));
    }

    private static IQueryable<Invoice> Ordered(IQueryable<Invoice> query, string order, string orderBy)
    {
        var desc = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);
        switch ((orderBy ?? "").ToLowerInvariant())
        {
            case "invoicedate":
                return desc
                    ? query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Number)
                    : query.OrderBy(i => i.InvoiceDate).ThenBy(i => i.Number);
            case "duedate":
                return desc
                    ? query.OrderByDescending(i => i.DueDate).ThenByDescending(i => i.Number)
                    : query.OrderBy(i => i.DueDate).ThenBy(i => i.Number);
            default:
                // Drafts have no number yet; they are the newest work, so they lead a
                // newest-first list and close an oldest-first one.
                return desc
                    ? query.OrderBy(i => i.Number == null ? 0 : 1).ThenByDescending(i => i.Number).ThenByDescending(i => i.CreatedAt)
                    : query.OrderBy(i => i.Number == null ? 1 : 0).ThenBy(i => i.Number).ThenBy(i => i.CreatedAt);
        }
    }

    private static async Task AttachAsync(WorkitDbContext db, List<Invoice> rows, string? include, CancellationToken ct)
    {
        var ids = rows.Select(i => i.Id).ToList();
        if (Includes(include, "lines"))
        {
            var lines = (await db.InvoiceLines.AsNoTracking().Where(l => ids.Contains(l.InvoiceId)).ToListAsync(ct)).ToLookup(l => l.InvoiceId);
            foreach (var row in rows) row.Lines = lines[row.Id].OrderBy(l => l.Position).ToList();
        }
        if (Includes(include, "payments"))
        {
            var payments = (await db.InvoicePayments.AsNoTracking().Where(p => ids.Contains(p.InvoiceId)).ToListAsync(ct)).ToLookup(p => p.InvoiceId);
            foreach (var row in rows) row.Payments = payments[row.Id].OrderBy(p => p.PaymentDate).ToList();
        }
    }

    // The invoice copy keeps the customer's name and SSN; the email (for "send by
    // email") comes from Workit's customer, which the customer sync keeps current.
    private static async Task<Dictionary<Guid, string?>> EmailsAsync(WorkitDbContext db, Guid companyId, List<Invoice> rows, CancellationToken ct)
    {
        var ids = rows.Select(i => i.CustomerPaydayId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await db.Customers.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.PaydayId != null && ids.Contains(c.PaydayId.Value))
                .Select(c => new { Id = c.PaydayId!.Value, c.Email })
                .ToListAsync(ct))
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Email);
    }

    private static PaydayInvoice ToPayday(Invoice i, string? include, Dictionary<Guid, string?> emails) => new()
    {
        Id          = i.PaydayId!.Value,
        Customer    = i.CustomerPaydayId is Guid customerId
            ? new PaydayCustomer { Id = customerId, Name = i.CustomerName, Ssn = i.CustomerSsn, Email = emails.GetValueOrDefault(customerId) }
            : null,
        Payor       = i.PayorPaydayId is Guid payorId
            ? new PaydayCustomer { Id = payorId, Name = i.PayorName, Ssn = i.PayorSsn }
            : null,
        Description = i.Description,
        Reference   = i.Reference,
        Number      = i.Number,
        Status      = i.Status,
        Created     = i.CreatedAt.UtcDateTime,

        ClaimCreated       = i.ClaimCreated,
        CancelDate         = i.CancelDate,
        ClaimFinalDueDate  = i.ClaimFinalDueDate,
        InvoiceDate        = i.InvoiceDate,
        DueDate            = i.DueDate,
        FinalDueDate       = i.FinalDueDate,
        PaidDate           = i.PaidDate,
        CancelledDate      = i.CancelledDate,
        RefundDate         = i.RefundDate,
        CreditDate         = i.CreditDate,
        SentDate           = i.SentDate,
        ClaimCancelledDate = i.ClaimCancelledDate,
        ClaimCancelled     = i.ClaimCancelled,

        AmountExcludingVat        = i.AmountExcludingVat,
        AmountIncludingVat        = i.AmountIncludingVat,
        AmountVat                 = i.AmountVat,
        ForeignAmountExcludingVat = i.ForeignAmountExcludingVat,
        ForeignAmountIncludingVat = i.ForeignAmountIncludingVat,
        ForeignAmountVat          = i.ForeignAmountVat,

        CurrencyCode             = i.CurrencyCode,
        CurrencyRate             = i.CurrencyRate,
        VatNumber                = i.VatNumber,
        CreateClaim              = i.CreateClaim,
        CreateElectronicInvoice  = i.CreateElectronicInvoice,
        ElectronicInvoicePartyId = i.ElectronicInvoicePartyId,
        AccountingCost           = i.AccountingCost,
        Ocr                      = i.Ocr,
        SendEmail                = i.SendEmail,
        DefaultInterest          = i.DefaultInterest,
        CapitalGainsTax          = i.CapitalGainsTax,

        Lines = Includes(include, "lines")
            ? i.Lines.Select(l => new PaydayInvoiceLine
            {
                Id                           = l.PaydayId ?? l.Id,
                InvoiceId                    = i.PaydayId!.Value,
                Position                     = l.Position,
                Description                  = l.Description,
                Comment                      = l.Comment,
                Quantity                     = l.Quantity,
                UnitPriceExcludingVat        = l.UnitPriceExcludingVat,
                UnitPriceIncludingVat        = l.UnitPriceIncludingVat,
                ForeignUnitPriceExcludingVat = l.ForeignUnitPriceExcludingVat,
                ForeignUnitPriceIncludingVat = l.ForeignUnitPriceIncludingVat,
                VatPercentage                = l.VatPercentage,
                DiscountPercentage           = l.DiscountPercentage,
                Sku                          = l.Sku,
                ProductId                    = l.ProductId,
            }).ToList()
            : null,
        Payments = Includes(include, "payments")
            ? i.Payments.Select(p => new PaydayInvoicePayment
            {
                Id            = p.PaydayId ?? p.Id,
                PaymentTypeId = p.PaymentTypePaydayId ?? Guid.Empty,
                PaymentDate   = p.PaymentDate,
                Amount        = p.Amount,
                CurrencyCode  = p.CurrencyCode,
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
