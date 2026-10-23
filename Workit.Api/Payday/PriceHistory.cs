using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Api.Payday;

/// <summary>
/// The price tracker: what each item was bought at, from which seller, and how the
/// Payday price list moved. <see cref="RecordPurchasesAsync"/> keeps
/// <c>PurchasePrices</c> in step with the stored purchases (run after every expense
/// sync); <c>PriceListChanges</c> are written where the price list changes —
/// <see cref="PurchasePriceListService"/> for Workit's own raises, the product sync
/// for changes made in Payday.
/// </summary>
internal static class PriceHistory
{
    /// <summary>
    /// One row per purchase line that starts with the vendor's item number, at its
    /// Ein.verð read the way the expense's total says. Rows for lines that are gone are
    /// dropped. Saves.
    /// </summary>
    public static async Task RecordPurchasesAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.Status != PaydayExpenseSyncService.DeletedStatus)
            .ToListAsync(ct);

        var wanted = new Dictionary<Guid, PurchasePrice>();
        foreach (var expense in expenses)
        {
            var reading = ExpenseUnitPrice.Read(expense.AmountExcludingVat, expense.Lines);
            foreach (var line in expense.Lines)
            {
                if (line.Quantity <= 0 || (line.UnitPriceExcludingVat ?? 0) <= 0) continue;
                if (PurchasePriceListService.ItemOf(line.Description) is not { } item) continue;
                var einVerd = ExpenseUnitPrice.SalePrice(line, reading);
                var discount = Math.Clamp(line.DiscountPercentage ?? 0m, 0m, 100m);
                wanted[line.Id] = new PurchasePrice
                {
                    CompanyId = companyId, ExpenseLineId = line.Id, ExpenseId = expense.Id,
                    Sku = item.Sku, Name = item.Name, Vendor = expense.CreditorName ?? "", PurchasedOn = expense.Date,
                    EinVerdExVat = einVerd, DiscountPercentage = discount,
                    PaidExVat = Math.Round(ExpenseUnitPrice.EinVerdExVat(line, reading) * (1m - discount / 100m), 2),
                    VatPercentage = line.VatPercentage, Quantity = line.Quantity,
                };
            }
        }

        var stored = await db.PurchasePrices.Where(p => p.CompanyId == companyId).ToListAsync(ct);
        var byLine = stored.ToDictionary(p => p.ExpenseLineId);
        foreach (var (lineId, row) in wanted)
        {
            if (byLine.TryGetValue(lineId, out var existing))
            {
                existing.Sku = row.Sku; existing.Name = row.Name; existing.Vendor = row.Vendor; existing.PurchasedOn = row.PurchasedOn;
                existing.EinVerdExVat = row.EinVerdExVat; existing.DiscountPercentage = row.DiscountPercentage; existing.PaidExVat = row.PaidExVat;
                existing.VatPercentage = row.VatPercentage; existing.Quantity = row.Quantity; existing.ExpenseId = row.ExpenseId;
            }
            else db.PurchasePrices.Add(row);
        }
        foreach (var gone in stored.Where(p => !wanted.ContainsKey(p.ExpenseLineId)))
            db.PurchasePrices.Remove(gone);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The items bought (optionally only from one seller, or matching a search), latest purchase first.</summary>
    public static async Task<List<TrackedItem>> ItemsAsync(WorkitDbContext db, Guid companyId, string? search, string? vendor, CancellationToken ct)
    {
        var query = db.PurchasePrices.AsNoTracking().Where(p => p.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(vendor)) query = query.Where(p => p.Vendor == vendor);
        var rows = await query.ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(p => p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var priceList = await PriceListAsync(db, companyId, ct);

        return rows.GroupBy(p => p.Sku)
            .Select(g =>
            {
                var ordered = Ordered(g).ToList();
                var latest = ordered[0];
                // The price before the latest one that differs from it — "how it moved".
                var previous = ordered.Skip(1).FirstOrDefault(p => p.EinVerdExVat != latest.EinVerdExVat);
                return new TrackedItem(g.Key, latest.Name, latest.EinVerdExVat, latest.Vendor, latest.PurchasedOn,
                    previous?.EinVerdExVat, previous?.PurchasedOn, g.Count(), g.Select(p => p.Vendor).Distinct().Count(),
                    priceList.TryGetValue(g.Key, out var listed) ? listed : null);
            })
            .OrderByDescending(i => i.LatestOn)
            .ThenBy(i => i.Sku, StringComparer.Ordinal)
            .ToList();
    }

    public static async Task<TrackedItemHistory?> ItemAsync(WorkitDbContext db, Guid companyId, string sku, CancellationToken ct)
    {
        var purchases = await db.PurchasePrices.AsNoTracking().Where(p => p.CompanyId == companyId && p.Sku == sku).ToListAsync(ct);
        var changes = await db.PriceListChanges.AsNoTracking().Where(c => c.CompanyId == companyId && c.Sku == sku)
            .OrderByDescending(c => c.ChangedAt).ToListAsync(ct);
        if (purchases.Count == 0 && changes.Count == 0) return null;
        var priceList = await PriceListAsync(db, companyId, ct);
        var ordered = Ordered(purchases).ToList();
        return new TrackedItemHistory(sku, ordered.FirstOrDefault()?.Name ?? changes.First().Name,
            priceList.TryGetValue(sku, out var listed) ? listed : null, ordered, changes);
    }

    /// <summary>Every seller, with how many items went up and down at their latest purchase.</summary>
    public static async Task<List<TrackedVendor>> VendorsAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var rows = await db.PurchasePrices.AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(ct);
        return rows.GroupBy(p => p.Vendor)
            .Select(v =>
            {
                int up = 0, down = 0;
                foreach (var item in v.GroupBy(p => p.Sku))
                {
                    var ordered = Ordered(item).ToList();
                    var previous = ordered.Skip(1).FirstOrDefault(p => p.EinVerdExVat != ordered[0].EinVerdExVat);
                    if (previous is null) continue;
                    if (ordered[0].EinVerdExVat > previous.EinVerdExVat) up++; else down++;
                }
                return new TrackedVendor(v.Key, v.Select(p => p.Sku).Distinct().Count(), v.Count(), v.Max(p => p.PurchasedOn), up, down);
            })
            .OrderByDescending(v => v.Purchases)
            .ToList();
    }

    public static Task<List<PriceListChange>> ChangesAsync(WorkitDbContext db, Guid companyId, int days, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
        return db.PriceListChanges.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.ChangedAt >= since)
            .OrderByDescending(c => c.ChangedAt)
            .ToListAsync(ct);
    }

    public static async Task<PriceTrackerSummary> SummaryAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var items = await ItemsAsync(db, companyId, null, null, ct);
        var vendors = await db.PurchasePrices.Where(p => p.CompanyId == companyId).Select(p => p.Vendor).Distinct().CountAsync(ct);
        var since90 = DateTime.UtcNow.AddDays(-90);
        var ups = items.Where(i => i.LatestOn >= since90 && i.ChangePercent > 0).Select(i => i.ChangePercent!.Value).Order().ToList();
        var monthAgo = DateTimeOffset.UtcNow.AddDays(-30);
        return new PriceTrackerSummary(
            items.Count,
            vendors,
            await db.PriceListChanges.CountAsync(c => c.CompanyId == companyId && c.ChangedAt >= monthAgo, ct),
            ups.Count,
            ups.Count == 0 ? null : ups[ups.Count / 2]);
    }

    /// <summary>Newest purchase first; the same day falls back to when it reached Workit.</summary>
    private static IOrderedEnumerable<PurchasePrice> Ordered(IEnumerable<PurchasePrice> rows) =>
        rows.OrderByDescending(p => p.PurchasedOn ?? DateTime.MinValue).ThenByDescending(p => p.RecordedAt);

    private static async Task<Dictionary<string, decimal>> PriceListAsync(WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        (await db.PaydayProducts.AsNoTracking().Where(p => p.CompanyId == companyId && !p.Archived && p.Sku != "")
            .Select(p => new { p.Sku, p.SalePriceExVat }).ToListAsync(ct))
        .GroupBy(p => p.Sku).ToDictionary(g => g.Key, g => Math.Round(g.First().SalePriceExVat, 0, MidpointRounding.AwayFromZero));
}
