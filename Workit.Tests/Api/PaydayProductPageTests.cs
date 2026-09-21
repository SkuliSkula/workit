using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>The paged product list the console reads: search, role filter, sort, paging, chip counts.</summary>
public class PaydayProductPageTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"pdpage-{Guid.NewGuid()}").Options);
        var now = DateTimeOffset.UtcNow;
        PaydayProductCache P(string sku, string name, decimal price, PaydayProductRole role, decimal? qty = null, string cat = "", bool archived = false) =>
            new() { CompanyId = CompanyId, PaydayId = Guid.NewGuid(), Sku = sku, Name = name, SalePriceExVat = price, Role = role, Quantity = qty, Category = cat, Archived = archived, SyncedAt = now };
        db.PaydayProducts.AddRange(
            P("0209621", "EKK 3G 1,5mm Plaststrengur", 258, PaydayProductRole.Unassigned, cat: "Strengir"),
            P("0209631", "EKK 3G 2,5mm Plaststrengur", 422, PaydayProductRole.Unassigned, cat: "Strengir"),
            P("AKSTUR", "Akstur", 2500, PaydayProductRole.DrivingUnit),
            P("DAGV", "Dagvinna rafvirkja", 11990, PaydayProductRole.RegularHour),
            P("FK016", "Festiklemma 16 mm", 480, PaydayProductRole.Material, qty: 95),
            P("OLD", "Gone product", 1, PaydayProductRole.Material, archived: true),
            new PaydayProductCache { CompanyId = Guid.NewGuid(), PaydayId = Guid.NewGuid(), Sku = "OTHER", Name = "Other company", Role = PaydayProductRole.Unassigned, SyncedAt = now });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task DefaultPage_IsLiveProductsOfTheCompany_BySku_WithRoleCounts()
    {
        await using var db = NewDb();
        var page = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, null, null, false, "sku", false, 1, 50, CancellationToken.None);

        page.Total.Should().Be(5);
        page.Items.Select(p => p.Sku).Should().Equal("0209621", "0209631", "AKSTUR", "DAGV", "FK016");
        page.RoleCounts[PaydayProductRole.Unassigned].Should().Be(2);
        page.RoleCounts[PaydayProductRole.Material].Should().Be(1, "archived products are not counted");
        page.RoleCounts.Should().NotContainKey(PaydayProductRole.OvertimeHour);
        page.LastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_OverSkuNameAndCategory_AndLeavesCountsAlone()
    {
        await using var db = NewDb();
        var byName = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, "plaststrengur", null, null, false, "sku", false, 1, 50, CancellationToken.None);
        var byCat  = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, "STRENGIR", null, null, false, "sku", false, 1, 50, CancellationToken.None);
        var bySku  = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, "fk0", null, null, false, "sku", false, 1, 50, CancellationToken.None);

        byName.Total.Should().Be(2);
        byCat.Total.Should().Be(2);
        bySku.Items.Should().ContainSingle().Which.Sku.Should().Be("FK016");
        bySku.RoleCounts[PaydayProductRole.Unassigned].Should().Be(2, "chips show the whole live set, not the search hits");
    }

    [Fact]
    public async Task RoleFilter_Sort_AndPaging()
    {
        await using var db = NewDb();
        var unassigned = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, PaydayProductRole.Unassigned, null, false, "price", true, 1, 50, CancellationToken.None);
        unassigned.Items.Select(p => p.Sku).Should().Equal("0209631", "0209621");

        var page2 = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, null, null, false, "name", false, 2, 10, CancellationToken.None);
        page2.PageSize.Should().Be(10, "the minimum page size is 10");
        page2.Items.Should().BeEmpty();
        page2.Total.Should().Be(5);

        var withArchived = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, null, null, true, "stock", true, 1, 50, CancellationToken.None);
        withArchived.Total.Should().Be(6);
        withArchived.Items.First().Sku.Should().Be("FK016", "tracked stock sorts before untracked");
    }

    [Fact]
    public async Task AssignUnset_TouchesOnlyLiveUnassignedProducts_OptionallyWithinASearch()
    {
        await using var db = NewDb();

        var narrowed = await PaydayProductEndpoints.AssignUnsetAsync(db, CompanyId, PaydayProductRole.Material, "1,5mm", CancellationToken.None);
        narrowed.Should().Be(1);
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "0209621")).Role.Should().Be(PaydayProductRole.Material);
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "0209631")).Role.Should().Be(PaydayProductRole.Unassigned);

        var rest = await PaydayProductEndpoints.AssignUnsetAsync(db, CompanyId, PaydayProductRole.Material, null, CancellationToken.None);
        rest.Should().Be(1);
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "DAGV")).Role.Should().Be(PaydayProductRole.RegularHour, "already assigned products are left alone");
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "OTHER")).Role.Should().Be(PaydayProductRole.Unassigned, "other companies are untouched");
        (await PaydayProductEndpoints.AssignUnsetAsync(db, CompanyId, PaydayProductRole.Material, null, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task CategoryFilter_AndCounts()
    {
        await using var db = NewDb();
        var strengir = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, null, "Strengir", false, "sku", false, 1, 50, CancellationToken.None);
        strengir.Total.Should().Be(2);
        strengir.CategoryCounts["Strengir"].Should().Be(2);
        strengir.CategoryCounts[""].Should().Be(3, "the uncategorised bucket");

        var none = await PaydayProductEndpoints.QueryPageAsync(db, CompanyId, null, null, PaydayProductEndpoints.UncategorisedFilter, false, "sku", false, 1, 50, CancellationToken.None);
        none.Total.Should().Be(3);
    }
}
