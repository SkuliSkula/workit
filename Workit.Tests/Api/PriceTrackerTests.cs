using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// The price tracker keeps every price an item was bought at, by seller, and every
/// change to the Payday price list — Workit's own raises and changes made in Payday.
/// </summary>
public class PriceTrackerTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private static readonly Guid GoodsAccount = Guid.NewGuid();
    private readonly PurchasePriceListTests.FakeProducts payday = new();

    private async Task<WorkitDbContext> NewDbAsync()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"price-tracker-{Guid.NewGuid()}").Options);
        db.Companies.Add(new Company { Id = companyId, Name = "ÓS" });
        await db.SaveChangesAsync();
        await PaydaySnapshotStore.SaveAsync(db, companyId, PaydaySnapshotStore.ExpenseAccounts,
            new List<PaydayExpenseAccount> { new() { Id = GoodsAccount, Name = "Vörukaup innanlands" } }, CancellationToken.None);
        return db;
    }

    private PaydayProductSyncService ProductSync(WorkitDbContext db) => new(db, payday, NullLogger<PaydayProductSyncService>.Instance);

    private PurchasePriceListService PriceList(WorkitDbContext db) => new(db, payday, ProductSync(db), NullLogger<PurchasePriceListService>.Instance);

    private Expense Bought(WorkitDbContext db, string description, decimal einVerd, string vendor = "Rönning", DateTime? on = null,
        decimal discount = 0m, decimal quantity = 1m, bool einVerdHasVat = false, string status = "PAID")
    {
        var expense = new Expense
        {
            CompanyId = companyId, PaydayId = Guid.NewGuid(), Source = DataSource.Payday, CreditorName = vendor, Status = status,
            Date = on ?? new DateTime(2026, 10, 2), CreatedAt = DateTimeOffset.UtcNow,
            AmountExcludingVat = Math.Round(quantity * einVerd * (1 - discount / 100), 2),
        };
        expense.Lines.Add(new ExpenseLine
        {
            ExpenseId = expense.Id, CompanyId = companyId, Description = description, Quantity = quantity,
            UnitPriceExcludingVat = einVerdHasVat ? einVerd * 1.24m : einVerd, DiscountPercentage = discount, VatPercentage = 24m, AccountId = GoodsAccount,
        });
        db.Expenses.Add(expense);
        db.SaveChanges();
        return expense;
    }

    [Fact]
    public async Task Every_purchase_line_with_an_item_number_is_kept_at_its_Ein_verd_in_whole_kronur()
    {
        await using var db = await NewDbAsync();
        Bought(db, "6268002 - PROFILED BORÐI 230V", 70395.16m, vendor: "Húsasmiðjan ehf.", discount: 45m, quantity: 2, einVerdHasVat: true);
        Bought(db, "Raflagnaefni", 12000m);
        Bought(db, "6087325 - GÚMMÍSTR.H07RN-F", -319m);   // a return
        Bought(db, "6053854 - KAPALSPENNA", 1039m, status: PaydayExpenseSyncService.DeletedStatus);

        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        var row = await db.PurchasePrices.SingleAsync();
        row.Should().BeEquivalentTo(new { Sku = "6268002", Name = "PROFILED BORÐI 230V", Vendor = "Húsasmiðjan ehf.",
            EinVerdExVat = 70395m, DiscountPercentage = 45m, PaidExVat = 38717.34m, Quantity = 2m });
    }

    [Fact]
    public async Task Recording_again_keeps_one_row_per_line_and_drops_lines_that_are_gone()
    {
        await using var db = await NewDbAsync();
        var kept = Bought(db, "6268002 - PROFILED BORÐI", 70000m);
        var gone = Bought(db, "6241620 - LED LAB U-FORM", 6780m);
        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        db.Expenses.Remove(gone);
        kept.Lines.Single().UnitPriceExcludingVat = 71000m;
        kept.AmountExcludingVat = 71000m;
        await db.SaveChangesAsync();
        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        (await db.PurchasePrices.SingleAsync()).Should().Match<PurchasePrice>(p => p.Sku == "6268002" && p.EinVerdExVat == 71000m);
    }

    [Fact]
    public async Task An_item_shows_its_latest_price_against_the_last_different_one()
    {
        await using var db = await NewDbAsync();
        Bought(db, "6268002 - PROFILED BORÐI", 60000m, on: new DateTime(2026, 6, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 65000m, vendor: "Reykjafell", on: new DateTime(2026, 8, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 65000m, on: new DateTime(2026, 9, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 71500m, on: new DateTime(2026, 10, 1));
        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        var item = (await PriceHistory.ItemsAsync(db, companyId, null, null, CancellationToken.None)).Single();

        item.Should().Match<TrackedItem>(i => i.LatestEinVerd == 71500m && i.PreviousEinVerd == 65000m && i.PreviousOn == new DateTime(2026, 9, 1)
            && i.Purchases == 4 && i.Vendors == 2 && i.LatestVendor == "Rönning");
        item.ChangePercent.Should().Be(10m);
        item.PriceListPriceExVat.Should().BeNull("it is not in the price list");
        (await PriceHistory.ItemAsync(db, companyId, "6268002", CancellationToken.None))!.Purchases.Select(p => p.EinVerdExVat)
            .Should().Equal(71500m, 65000m, 65000m, 60000m);
    }

    [Fact]
    public async Task By_seller_each_sellers_own_prices_are_compared()
    {
        await using var db = await NewDbAsync();
        Bought(db, "6268002 - PROFILED BORÐI", 60000m, vendor: "Rönning", on: new DateTime(2026, 6, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 66000m, vendor: "Rönning", on: new DateTime(2026, 9, 1));
        Bought(db, "6241620 - LED LAB U-FORM", 7000m, vendor: "Rönning", on: new DateTime(2026, 6, 1));
        Bought(db, "6241620 - LED LAB U-FORM", 6500m, vendor: "Rönning", on: new DateTime(2026, 9, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 58000m, vendor: "Reykjafell", on: new DateTime(2026, 10, 1));
        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        var vendors = await PriceHistory.VendorsAsync(db, companyId, CancellationToken.None);
        vendors.Single(v => v.Vendor == "Rönning").Should().Match<TrackedVendor>(v => v.Items == 2 && v.Purchases == 4 && v.ItemsUp == 1 && v.ItemsDown == 1);
        vendors.Single(v => v.Vendor == "Reykjafell").Should().Match<TrackedVendor>(v => v.ItemsUp == 0 && v.ItemsDown == 0);

        var fromRonning = await PriceHistory.ItemsAsync(db, companyId, null, "Rönning", CancellationToken.None);
        fromRonning.Single(i => i.Sku == "6268002").LatestEinVerd.Should().Be(66000m, "the other seller's later price is not Rönning's");
        (await PriceHistory.ItemsAsync(db, companyId, "led lab", null, CancellationToken.None)).Select(i => i.Sku).Should().Equal("6241620");
    }

    [Fact]
    public async Task Workits_raises_and_new_products_are_recorded_with_who_applied_them_and_not_again_by_the_sync()
    {
        await using var db = await NewDbAsync();
        var product = new PaydayProduct { Id = Guid.NewGuid(), Sku = "6268002", Name = "Profiled borði", SalesUnitPriceExcludingVAT = 65000m, VatPercentage = 24, Tags = [] };
        payday.Products.Add(product);
        await ProductSync(db).SyncAsync(companyId, CancellationToken.None);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m, vendor: "Húsasmiðjan ehf.", on: new DateTime(2026, 10, 20));
        Bought(db, "6241620 - LED LAB U-FORM", 6781m);
        var service = PriceList(db);

        await service.ApplyAsync(companyId, await service.PlanAsync(companyId, null, CancellationToken.None), CancellationToken.None, "Skúli");
        await ProductSync(db).SyncAsync(companyId, CancellationToken.None);

        var changes = await db.PriceListChanges.OrderBy(c => c.Sku).ToListAsync();
        changes.Should().HaveCount(2);
        changes[0].Should().Match<PriceListChange>(c => c.Sku == "6241620" && c.OldPriceExVat == null && c.NewPriceExVat == 6781m && c.Source == PriceChangeSource.Purchase && c.By == "Skúli");
        changes[1].Should().BeEquivalentTo(new { Sku = "6268002", OldPriceExVat = (decimal?)65000m, NewPriceExVat = 70395m,
            Vendor = "Húsasmiðjan ehf.", PurchasedOn = (DateTime?)new DateTime(2026, 10, 20), By = "Skúli" });
    }

    [Fact]
    public async Task A_price_changed_in_Payday_is_recorded_but_not_a_companys_first_sync()
    {
        await using var db = await NewDbAsync();
        var product = new PaydayProduct { Id = Guid.NewGuid(), Sku = "6268002", Name = "Profiled borði", SalesUnitPriceExcludingVAT = 65000m, VatPercentage = 24, Tags = [] };
        payday.Products.Add(product);
        await ProductSync(db).SyncAsync(companyId, CancellationToken.None);
        (await db.PriceListChanges.CountAsync()).Should().Be(0, "the first sync is the starting point, not a change");

        product.SalesUnitPriceExcludingVAT = 68000.4m;
        payday.Products.Add(new PaydayProduct { Id = Guid.NewGuid(), Sku = "7777001", Name = "Varrofi", SalesUnitPriceExcludingVAT = 1450m, VatPercentage = 24, Tags = [] });
        await ProductSync(db).SyncAsync(companyId, CancellationToken.None);
        await ProductSync(db).SyncAsync(companyId, CancellationToken.None);

        var changes = await db.PriceListChanges.OrderBy(c => c.Sku).ToListAsync();
        changes.Should().HaveCount(2);
        changes[0].Should().Match<PriceListChange>(c => c.Sku == "6268002" && c.OldPriceExVat == 65000m && c.NewPriceExVat == 68000m && c.Source == PriceChangeSource.Payday);
        changes[1].Should().Match<PriceListChange>(c => c.Sku == "7777001" && c.OldPriceExVat == null && c.NewPriceExVat == 1450m);
    }

    [Fact]
    public async Task The_summary_counts_items_that_went_up_with_the_median_increase()
    {
        await using var db = await NewDbAsync();
        var recent = DateTime.UtcNow.Date.AddDays(-10);
        Bought(db, "1001 - A", 100m, on: recent.AddDays(-30), vendor: "Ískraft");
        Bought(db, "1001 - A", 110m, on: recent);
        Bought(db, "1002 - B", 100m, on: recent.AddDays(-30));
        Bought(db, "1002 - B", 120m, on: recent);
        Bought(db, "1003 - C", 100m, on: recent.AddDays(-30));
        Bought(db, "1003 - C", 130m, on: recent);
        Bought(db, "1004 - D", 100m, on: recent.AddDays(-30));
        Bought(db, "1004 - D", 90m, on: recent, vendor: "Reykjafell");
        await PriceHistory.RecordPurchasesAsync(db, companyId, CancellationToken.None);

        var summary = await PriceHistory.SummaryAsync(db, companyId, CancellationToken.None);

        summary.Should().Match<PriceTrackerSummary>(s => s.Items == 4 && s.Vendors == 3 && s.ItemsUp90Days == 3 && s.MedianIncreasePercent90Days == 20m);
    }
}
