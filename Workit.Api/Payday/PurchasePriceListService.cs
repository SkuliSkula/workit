using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Keeps the company's Payday price list in step with what it buys. Prices in
/// Iceland only go up, and the vendor's Ein.verð on each purchase is the freshest
/// price there is: an item bought at a higher Ein.verð than its product's price
/// raises that price in Payday, and an item not in the price list is created there
/// (as a Material, without stock tracking). Prices are never lowered.
/// <para>
/// Only goods bought for resale count — lines posted to the "Vörukaup innanlands"
/// account that start with the vendor's item number ("6268002 - PROFILED …"). The
/// Ein.verð is read the way the expense's own total says (<see cref="ExpenseUnitPrice"/>).
/// Each item is judged by its latest purchase.
/// </para>
/// This writes to the company's live Payday, so it starts only when the owner
/// applies a preview (<see cref="ApplyAsync"/>), and afterwards it acts only on
/// purchases entered after that moment — a price lowered or a product archived by
/// hand is never undone by an older purchase. Callers set the company's Payday
/// credentials on the token service first.
/// </summary>
public sealed class PurchasePriceListService(
    WorkitDbContext db,
    IPaydayProductsApi payday,
    PaydayProductSyncService productSync,
    ILogger<PurchasePriceListService> logger)
{
    /// <summary>The expense account goods for resale are posted to.</summary>
    public const string GoodsAccountName = "Vörukaup innanlands";

    private const string Kind = "price-list";

    /// <summary>
    /// The vendor's item number: what comes before the first " - " of a line's
    /// description, with a digit in it — "6268002", "SG 614393", "221_415", "BLA-360210309900-M".
    /// The price list writes item numbers exactly the vendors' way.
    /// </summary>
    public static (string Sku, string Name)? ItemOf(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var cut = description.IndexOf(" - ", StringComparison.Ordinal);
        if (cut <= 0) return null;
        var sku = description[..cut].Trim();
        var name = description[(cut + 3)..].Trim();
        // Item numbers are codes: a digit in them, and no lowercase — every one of 841 in
        // ÓS's price list is. "Alhef fur/gre 21x58 húsðurr - 420" is a name, not a number.
        if (sku.Length is < 2 or > 30 || name.Length == 0 || !sku.Any(char.IsDigit) || sku.Any(char.IsLower)) return null;
        return (sku, name);
    }

    /// <summary>
    /// Words a vendor uses for a charge rather than goods — delivery, rounding,
    /// discounts, fees. Such lines start unticked in the preview; the owner decides.
    /// </summary>
    private static readonly string[] ChargeWords = ["akstur", "flutning", "sending", "auramun", "sléttun", "afslátt", "gjald", "umbúð"];

    public static bool LooksLikeCharge(string name) =>
        ChargeWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>The company's "Vörukaup innanlands" accounts, from the stored Payday expense accounts.</summary>
    internal static async Task<HashSet<Guid>> GoodsAccountsAsync(WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        ((await PaydaySnapshotStore.GetAsync<List<PaydayExpenseAccount>>(db, companyId, PaydaySnapshotStore.ExpenseAccounts, ct)) ?? [])
            .Where(a => string.Equals(a.Name?.Trim(), GoodsAccountName, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToHashSet();

    internal static async Task<HashSet<string>> IgnoredAsync(WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.PriceListIgnoredSkus).FirstOrDefaultAsync(ct) is { } list
            ? IgnoredOf(new Company { PriceListIgnoredSkus = list })
            : [];

    public static HashSet<string> IgnoredOf(Company company) =>
        company.PriceListIgnoredSkus.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What applying would do, read from purchases entered since <paramref name="since"/>
    /// (all of them when null). Writes nothing.
    /// </summary>
    public async Task<PriceListPlan> PlanAsync(Guid companyId, DateTimeOffset? since, CancellationToken ct)
    {
        var goods = await GoodsAccountsAsync(db, companyId, ct);
        if (goods.Count == 0)
            return new([], [], [], since, $"Payday's expense accounts have no \"{GoodsAccountName}\" account yet — wait for the next sync.");

        var ignored = await IgnoredAsync(db, companyId, ct);

        var query = db.Expenses.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.Status != PaydayExpenseSyncService.DeletedStatus);
        if (since is DateTimeOffset s) query = query.Where(e => e.CreatedAt >= s);
        var expenses = await query.ToListAsync(ct);

        // The latest purchase of each item, at its Ein.verð.
        var latest = new Dictionary<string, (string Name, decimal Price, decimal Vat, string Vendor, DateTime? On, DateTimeOffset Entered)>();
        foreach (var expense in expenses)
        {
            var reading = ExpenseUnitPrice.Read(expense.AmountExcludingVat, expense.Lines);
            foreach (var line in expense.Lines)
            {
                if (line.AccountId is not Guid account || !goods.Contains(account)) continue;
                if (line.Quantity <= 0 || (line.UnitPriceExcludingVat ?? 0) <= 0) continue;   // returns, credits, free items
                if (ItemOf(line.Description) is not { } item || ignored.Contains(item.Sku)) continue;
                var price = ExpenseUnitPrice.SalePrice(line, reading);
                if (price <= 0) continue;
                var candidate = (item.Name, price, line.VatPercentage, expense.CreditorName ?? "", expense.Date, expense.CreatedAt);
                if (!latest.TryGetValue(item.Sku, out var current)
                    || (expense.Date ?? DateTime.MinValue, expense.CreatedAt).CompareTo((current.On ?? DateTime.MinValue, current.Entered)) > 0)
                    latest[item.Sku] = candidate;
            }
        }

        var products = await db.PaydayProducts.AsNoTracking().Where(p => p.CompanyId == companyId && p.Sku != "").ToListAsync(ct);
        var live = products.Where(p => !p.Archived).GroupBy(p => p.Sku).ToDictionary(g => g.Key, g => g.First());
        var archived = products.Where(p => p.Archived).Select(p => p.Sku).ToHashSet();

        var raises = new List<PriceRaise>();
        var additions = new List<PriceListAddition>();
        var archivedBought = new List<string>();
        foreach (var (sku, bought) in latest.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (live.TryGetValue(sku, out var product))
            {
                var currentPrice = Math.Round(product.SalePriceExVat, 0, MidpointRounding.AwayFromZero);
                if (bought.Price > currentPrice)
                    raises.Add(new PriceRaise(product.PaydayId, sku, product.Name, currentPrice, bought.Price, bought.Vendor, bought.On, LooksLikeCharge(bought.Name)));
            }
            else if (archived.Contains(sku))
                archivedBought.Add(sku);
            else
                additions.Add(new PriceListAddition(sku, bought.Name, bought.Price, bought.Vat, bought.Vendor, bought.On, LooksLikeCharge(bought.Name)));
        }
        return new PriceListPlan(raises, additions, archivedBought, since, IgnoredCount: ignored.Count);
    }

    /// <summary>
    /// Raises and creates in Payday what the plan says, refreshes the product cache,
    /// and marks the created products as Materials. The first apply also switches the
    /// feature on: from then on <see cref="SyncAsync"/> runs it for new purchases.
    /// </summary>
    /// <param name="by">Who applied it — the owner's name, or "Automatic" for the background run. Kept on each change.</param>
    public async Task<PriceListApplyResult> ApplyAsync(Guid companyId, PriceListPlan plan, CancellationToken ct, string by = "Automatic")
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var company = await db.Companies.FirstAsync(c => c.Id == companyId, ct);
            company.PriceListFromPurchasesSince ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var failed = new List<string>();
            int raised = 0, created = 0;

            foreach (var raise in plan.Raises)
            {
                // Fresh from Payday, so the update carries Payday's own values, not the cache's.
                var current = await payday.GetByIdAsync(raise.PaydayProductId);
                if (current is not { IsSuccess: true, Value: { } product })
                {
                    failed.Add($"{raise.Sku}: {current.ErrorMessage ?? "not found in Payday"}");
                    continue;
                }
                if ((product.SalesUnitPriceExcludingVAT ?? 0) >= raise.NewPriceExVat) continue;   // already raised meanwhile
                var oldPrice = Math.Round(product.SalesUnitPriceExcludingVAT ?? 0m, 0, MidpointRounding.AwayFromZero);
                var updated = await payday.UpdateAsync(product.Id, new UpdateProductRequest
                {
                    Name                       = product.Name ?? raise.Name,
                    Description                = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
                    Sku                        = product.Sku ?? raise.Sku,
                    SalesUnitPriceExcludingVAT = raise.NewPriceExVat,
                    VatPercentage              = product.VatPercentage ?? 24m,
                    SalesLedgerAccountId       = product.SalesLedgerAccountId,
                    Tags                       = product.Tags,
                    Archived                   = product.Archived,
                });
                if (updated.IsSuccess)
                {
                    raised++;
                    db.PriceListChanges.Add(new PriceListChange
                    {
                        CompanyId = companyId, PaydayProductId = product.Id, Sku = raise.Sku, Name = product.Name ?? raise.Name,
                        OldPriceExVat = oldPrice, NewPriceExVat = raise.NewPriceExVat,
                        Source = PriceChangeSource.Purchase, Vendor = raise.Vendor, PurchasedOn = raise.PurchasedOn, By = by,
                    });
                    // The cache follows at once, so the product sync doesn't see this as a change made in Payday.
                    var cachedRow = await db.PaydayProducts.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.PaydayId == product.Id, ct);
                    if (cachedRow is not null) cachedRow.SalePriceExVat = raise.NewPriceExVat;
                    await db.SaveChangesAsync(ct);
                }
                else failed.Add($"{raise.Sku}: {updated.ErrorMessage}");
            }

            List<PaydayLedgerAccount> ledgers = [];
            if (plan.Additions.Count > 0)
            {
                var ledgerResult = await PaydaySnapshotStore.LiveOrStoredAsync(
                    await payday.GetSalesLedgerAccountsAsync(), db, companyId, PaydaySnapshotStore.LedgerAccounts, ct);
                ledgers = (ledgerResult.Value ?? []).Where(a => !a.Archived).ToList();
            }
            var createdSkus = new HashSet<string>();
            foreach (var addition in plan.Additions)
            {
                var ledger = LedgerFor(ledgers, addition.VatPercentage);
                if (ledger is null)
                {
                    failed.Add($"{addition.Sku}: no sales account in Payday for {addition.VatPercentage:0.#} % VAT");
                    continue;
                }
                var result = await payday.CreateAsync(new CreateProductRequest
                {
                    Name                       = addition.Name,
                    Sku                        = addition.Sku,
                    Quantity                   = null,   // no stock tracking
                    SalesUnitPriceExcludingVAT = addition.PriceExVat,
                    VatPercentage              = addition.VatPercentage,
                    SalesLedgerAccountId       = ledger.Id,
                    Tags                       = ["workit"],
                });
                if (result is { IsSuccess: true, Value: { } made })
                {
                    created++;
                    createdSkus.Add(addition.Sku);
                    db.PriceListChanges.Add(new PriceListChange
                    {
                        CompanyId = companyId, PaydayProductId = made.Id, Sku = addition.Sku, Name = addition.Name,
                        OldPriceExVat = null, NewPriceExVat = addition.PriceExVat,
                        Source = PriceChangeSource.Purchase, Vendor = addition.Vendor, PurchasedOn = addition.PurchasedOn, By = by,
                    });
                    await db.SaveChangesAsync(ct);
                }
                else failed.Add($"{addition.Sku}: {result.ErrorMessage}");
            }

            if (raised + created > 0)
            {
                // Pull the changes in, then make the new products usable by the crew.
                await productSync.SyncAsync(companyId, ct);
                if (createdSkus.Count > 0)
                {
                    var cached = await db.PaydayProducts
                        .Where(p => p.CompanyId == companyId && !p.Archived && createdSkus.Contains(p.Sku))
                        .ToListAsync(ct);
                    foreach (var p in cached) p.Role = PaydayProductRole.Material;
                    await db.SaveChangesAsync(ct);
                    if (company.MaterialsManagedInPayday)
                        await productSync.MirrorMaterialsAsync(companyId, await db.PaydayProducts.Where(p => p.CompanyId == companyId).ToListAsync(ct), ct);
                }
            }

            logger.LogInformation("Price list from purchases for {Company}: {Raised} raised, {Created} created, {Failed} refused",
                companyId, raised, created, failed.Count);
            return new PriceListApplyResult(raised, created, failed, company.PriceListFromPurchasesSince.Value);
        }
    }

    /// <summary>Adds item numbers to the ones the owner left out; they never come up again.</summary>
    public async Task IgnoreAsync(Guid companyId, IEnumerable<string> skus, CancellationToken ct)
    {
        var company = await db.Companies.FirstAsync(c => c.Id == companyId, ct);
        var ignored = IgnoredOf(company);
        foreach (var sku in skus.Select(s => s.Trim()).Where(s => s.Length > 0)) ignored.Add(sku);
        company.PriceListIgnoredSkus = string.Join('\n', ignored.Order(StringComparer.Ordinal));
        await db.SaveChangesAsync(ct);
        await PriceHistory.RecordPurchasesAsync(db, companyId, ct);   // an unticked item leaves the price tracker too
    }

    /// <summary>The background run: once switched on, act on purchases entered since then. Null when off or nothing to do.</summary>
    public async Task<PriceListApplyResult?> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var since = await db.Companies.Where(c => c.Id == companyId).Select(c => c.PriceListFromPurchasesSince).FirstOrDefaultAsync(ct);
        if (since is null) return null;
        var plan = await PlanAsync(companyId, since, ct);
        // Unattended, a line that looks like a charge is left for the owner to see in the preview.
        plan = plan with
        {
            Raises    = plan.Raises.Where(r => !r.LooksLikeCharge).ToList(),
            Additions = plan.Additions.Where(a => !a.LooksLikeCharge).ToList(),
        };
        if (plan.Problem is not null || plan.Raises.Count + plan.Additions.Count == 0) return null;
        return await ApplyAsync(companyId, plan, ct);
    }

    /// <summary>The sales account for a VAT rate — Payday doesn't check a line's VAT against its account.</summary>
    internal static PaydayLedgerAccount? LedgerFor(List<PaydayLedgerAccount> ledgers, decimal vat)
    {
        var wanted = vat switch { 24m => "StandardVAT", 11m => "ReducedVAT", _ => "" };
        return ledgers.FirstOrDefault(a => (a.DefaultVatPercentType ?? "") == wanted);
    }
}
