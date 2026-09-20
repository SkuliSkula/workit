using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// One-time switch-over of a company's materials to Payday products. Matches
/// by SKU (Workit's product code = Payday's sku), creates what Payday lacks
/// with Workit's current stock as the opening balance, links everything, then
/// turns on <see cref="Company.MaterialsManagedInPayday"/> and mirrors. A dry
/// run reports the same rows without touching anything.
/// </summary>
public sealed class MaterialsMigrationService(WorkitDbContext db, IPaydayProductsApi payday, PaydayProductSyncService sync, ILogger<MaterialsMigrationService> logger)
{
    public async Task<MaterialsMigrationResult> RunAsync(Guid companyId, bool dryRun, CancellationToken ct)
    {
        var rows = new List<MaterialsMigrationRow>();

        // Fresh view of Payday first, so we never create a product that exists.
        var synced = await sync.SyncAsync(companyId, ct);
        if (synced.Error is not null)
            return new MaterialsMigrationResult(dryRun, rows, 0, 0, 0, 0, false, synced.Error);

        var products  = await db.PaydayProducts.Where(p => p.CompanyId == companyId && !p.Archived).ToListAsync(ct);
        var bySku     = products.Where(p => p.Sku.Length > 0).GroupBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var materials = await db.Materials.Where(m => m.CompanyId == companyId && m.IsActive).OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync(ct);

        // Duplicate product codes inside Workit cannot map to Payday's unique SKU.
        var duplicateCodes = materials.Where(m => m.ProductCode.Trim().Length > 0)
            .GroupBy(m => m.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Guid? ledgerAccount = null;
        var plan = new List<(Material material, MaterialsMigrationAction action, PaydayProductCache? product, string? message)>();
        foreach (var m in materials)
        {
            var code = m.ProductCode.Trim();
            if (m.PaydayProductId is Guid linkedId && products.Any(p => p.PaydayId == linkedId))
                plan.Add((m, MaterialsMigrationAction.AlreadyLinked, products.First(p => p.PaydayId == linkedId), null));
            else if (code.Length == 0)
                plan.Add((m, MaterialsMigrationAction.Conflict, null, "No product code — give it one (it becomes the Payday SKU) and run again."));
            else if (duplicateCodes.Contains(code))
                plan.Add((m, MaterialsMigrationAction.Conflict, null, $"Product code {code} is used by more than one material; Payday SKUs are unique."));
            else if (bySku.TryGetValue(code, out var matches) && matches.Count == 1)
                plan.Add((m, MaterialsMigrationAction.Link, matches[0], matches[0].Role == PaydayProductRole.Material ? null : $"Product {code} exists in Payday with the role {matches[0].Role}; it will be set to Material."));
            else if (matches is { Count: > 1 })
                plan.Add((m, MaterialsMigrationAction.Conflict, null, $"Payday has {matches.Count} products with SKU {code}."));
            else
                plan.Add((m, MaterialsMigrationAction.Create, null, $"Created in Payday with {m.Quantity:0.##} {m.Unit} as opening stock at {m.UnitPrice:N0} kr."));
        }

        int linked = 0, created = 0, already = 0, conflicts = 0;
        foreach (var (m, action, product, message) in plan)
        {
            var sku = product?.Sku ?? (action == MaterialsMigrationAction.Create ? m.ProductCode.Trim() : null);
            rows.Add(new MaterialsMigrationRow(m.Id, m.Name, m.ProductCode, action, sku, message));
            switch (action)
            {
                case MaterialsMigrationAction.AlreadyLinked: already++; break;
                case MaterialsMigrationAction.Conflict: conflicts++; break;
                case MaterialsMigrationAction.Link:
                    linked++;
                    if (!dryRun)
                    {
                        m.PaydayProductId = product!.PaydayId;
                        if (product.Role != PaydayProductRole.Material) product.Role = PaydayProductRole.Material;
                        if (string.IsNullOrWhiteSpace(product.Unit)) product.Unit = m.Unit;
                        if (string.IsNullOrWhiteSpace(product.Category)) product.Category = m.Category;
                    }
                    break;
                case MaterialsMigrationAction.Create:
                    created++;
                    if (!dryRun)
                    {
                        ledgerAccount ??= await PickSalesLedgerAccountAsync(products, ct);
                        if (ledgerAccount is null)
                        {
                            rows[^1] = rows[^1] with { Action = MaterialsMigrationAction.Conflict, Message = "No sales ledger account found in Payday to book the product to." };
                            created--; conflicts++;
                            break;
                        }
                        var createResult = await payday.CreateAsync(new CreateProductRequest
                        {
                            Name = m.Name, Description = m.Description, Sku = m.ProductCode.Trim(),
                            Quantity = m.Quantity, SalesUnitPriceExcludingVAT = m.UnitPrice, VatPercentage = m.VatRate,
                            SalesLedgerAccountId = ledgerAccount.Value,
                            PurchaseDate = DateTime.Today.ToString("yyyy-MM-dd"),
                            PurchaseUnitPriceExcludingVAT = m.PurchasePrice > 0 ? m.PurchasePrice : null,
                            Tags = ["workit"],
                        });
                        if (!createResult.IsSuccess || createResult.Value is null)
                        {
                            rows[^1] = rows[^1] with { Action = MaterialsMigrationAction.Conflict, Message = createResult.ErrorMessage ?? "Payday refused to create the product." };
                            created--; conflicts++;
                            break;
                        }
                        m.PaydayProductId = createResult.Value.Id;
                        db.PaydayProducts.Add(new PaydayProductCache
                        {
                            CompanyId = companyId, PaydayId = createResult.Value.Id, Sku = m.ProductCode.Trim(), Name = m.Name, Description = m.Description,
                            SalePriceExVat = m.UnitPrice, SalePriceIncVat = Math.Round(m.UnitPrice * (1 + m.VatRate / 100m), 2), VatPercentage = m.VatRate,
                            SalesLedgerAccountId = ledgerAccount, Quantity = m.Quantity, Role = PaydayProductRole.Material, Unit = m.Unit, Category = m.Category,
                            SyncedAt = DateTimeOffset.UtcNow,
                        });
                    }
                    break;
            }
        }

        if (dryRun)
            return new MaterialsMigrationResult(true, rows, linked, created, already, conflicts, false, null);

        await db.SaveChangesAsync(ct);

        // Switch over only when nothing is left unresolved — a half-managed list is worse than either.
        var switchedOver = conflicts == 0;
        if (switchedOver)
        {
            var company = await db.Companies.FirstAsync(c => c.Id == companyId, ct);
            company.MaterialsManagedInPayday = true;
            await db.SaveChangesAsync(ct);
            var cache = await db.PaydayProducts.Where(p => p.CompanyId == companyId).ToListAsync(ct);
            await sync.MirrorMaterialsAsync(companyId, cache, ct);
        }
        logger.LogInformation("Materials migration for {CompanyId}: linked {Linked}, created {Created}, conflicts {Conflicts}, switched over {Switched}", companyId, linked, created, conflicts, switchedOver);
        return new MaterialsMigrationResult(false, rows, linked, created, already, conflicts, switchedOver, null);
    }

    /// <summary>The account the company's existing material products sell to, else Payday's 24 % sales account.</summary>
    private async Task<Guid?> PickSalesLedgerAccountAsync(List<PaydayProductCache> products, CancellationToken ct)
    {
        var fromProducts = products.Where(p => p.Role == PaydayProductRole.Material && p.SalesLedgerAccountId is not null)
            .GroupBy(p => p.SalesLedgerAccountId).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        if (fromProducts is not null) return fromProducts;

        var accounts = await payday.GetSalesLedgerAccountsAsync();
        if (!accounts.IsSuccess || accounts.Value is null) return null;
        var live = accounts.Value.Where(a => !a.Archived).ToList();
        return (live.FirstOrDefault(a => a.DefaultVatPercentType == "StandardVAT") ?? live.FirstOrDefault())?.Id;
    }
}
