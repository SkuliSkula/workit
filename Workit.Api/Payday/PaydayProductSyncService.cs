using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Payday → Workit for products: refreshes the per-company cache and, for a
/// company whose materials are managed in Payday, mirrors the Material-role
/// products into <c>Materials</c> so every existing screen and app keeps
/// working unchanged. Called by the sync endpoint and by the background sync.
/// Callers set the company's Payday credentials on the token service first.
/// </summary>
public sealed class PaydayProductSyncService(WorkitDbContext db, IPaydayProductsApi payday, ILogger<PaydayProductSyncService> logger)
{
    public sealed record Outcome(PaydayProductSyncResult? Result, string? Error);

    public async Task<Outcome> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var fetched = new List<PaydayProduct>();
        for (var page = 1; ; page++)
        {
            var result = await payday.GetAllAsync(page: page, perPage: 100);
            if (!result.IsSuccess || result.Value is null)
                return new Outcome(null, result.ErrorMessage ?? "Payday products could not be loaded right now.");
            fetched.AddRange(result.Value.Products);
            if (page >= result.Value.Pages || result.Value.Products.Count == 0) break;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.PaydayProducts.Where(p => p.CompanyId == companyId).ToListAsync(ct);
        var byPaydayId = existing.ToDictionary(p => p.PaydayId);
        int added = 0, updated = 0, archived = 0;

        foreach (var remote in fetched)
        {
            if (byPaydayId.TryGetValue(remote.Id, out var local))
            {
                Apply(local, remote, now);
                updated++;
            }
            else
            {
                local = new PaydayProductCache { CompanyId = companyId, PaydayId = remote.Id };
                Apply(local, remote, now);
                // A product Payday tracks stock for is a material until the owner says otherwise.
                local.Role = remote.Quantity is not null ? PaydayProductRole.Material : PaydayProductRole.Unassigned;
                db.PaydayProducts.Add(local);
                existing.Add(local);
                added++;
            }
        }

        var remoteIds = fetched.Select(r => r.Id).ToHashSet();
        foreach (var gone in existing.Where(p => !remoteIds.Contains(p.PaydayId) && !p.Archived))
        {
            gone.Archived = true;
            gone.SyncedAt = now;
            archived++;
        }

        await db.SaveChangesAsync(ct);

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company?.MaterialsManagedInPayday == true)
            await MirrorMaterialsAsync(companyId, existing, ct);

        return new Outcome(new PaydayProductSyncResult(fetched.Count, added, updated, archived, now), null);
    }

    /// <summary>
    /// Materials = the Material-role products. Upserts by Payday id: name, code,
    /// prices, VAT, unit/category and — the point of it — Payday's stock
    /// quantity. Materials whose product is gone or archived go inactive, never
    /// deleted (usages point at them). Materials with no Payday link are left
    /// alone; the migration is what links them.
    /// </summary>
    public async Task MirrorMaterialsAsync(Guid companyId, List<PaydayProductCache> cache, CancellationToken ct)
    {
        var materials = await db.Materials.Where(m => m.CompanyId == companyId).ToListAsync(ct);
        var byProduct = materials.Where(m => m.PaydayProductId is not null).ToDictionary(m => m.PaydayProductId!.Value);
        var products = cache.Where(p => p.Role == PaydayProductRole.Material).ToList();

        foreach (var p in products)
        {
            if (!byProduct.TryGetValue(p.PaydayId, out var m))
            {
                if (p.Archived) continue;
                m = new Material { CompanyId = companyId, PaydayProductId = p.PaydayId, MarkupFactor = 1m, Unit = "stk." };
                db.Materials.Add(m);
                byProduct[p.PaydayId] = m;
            }
            m.Name        = p.Name;
            m.ProductCode = p.Sku;
            m.Description = p.Description;
            m.UnitPrice   = p.SalePriceExVat;
            m.VatRate     = p.VatPercentage;
            m.Quantity    = p.Quantity ?? 0m;
            m.IsActive    = !p.Archived;
            if (!string.IsNullOrWhiteSpace(p.Unit))     m.Unit     = p.Unit;
            if (!string.IsNullOrWhiteSpace(p.Category)) m.Category = p.Category;
            // Payday owns tags outright, so an emptied tag list clears them here too.
            m.Tags = p.Tags;
        }

        // Linked to a product that no longer carries the Material role → inactive.
        var materialIds = products.Select(p => p.PaydayId).ToHashSet();
        foreach (var m in materials.Where(m => m.PaydayProductId is Guid id && !materialIds.Contains(id) && m.IsActive))
            m.IsActive = false;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Mirrored {Count} Payday products into materials for company {CompanyId}", products.Count, companyId);
    }

    private static void Apply(PaydayProductCache local, PaydayProduct remote, DateTimeOffset now)
    {
        local.Sku                  = remote.Sku?.Trim() ?? string.Empty;
        local.Name                 = remote.Name?.Trim() ?? string.Empty;
        local.Description          = remote.Description?.Trim() ?? string.Empty;
        local.SalePriceExVat       = remote.SalesUnitPriceExcludingVAT ?? 0m;
        local.SalePriceIncVat      = remote.SalesUnitPriceIncludingVAT ?? 0m;
        local.VatPercentage        = remote.VatPercentage ?? 0m;
        local.SalesLedgerAccountId = remote.SalesLedgerAccountId;
        local.Quantity             = remote.Quantity;
        local.Archived             = remote.Archived;
        local.Tags                 = remote.Tags is { Count: > 0 } ? string.Join(",", remote.Tags) : string.Empty;
        local.SyncedAt             = now;
    }
}
