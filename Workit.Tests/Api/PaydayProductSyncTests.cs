using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// The Payday products sync: pages through Payday, upserts the cache, keeps
/// Workit's own fields, archives what Payday stopped returning, and defaults
/// the role from whether Payday tracks stock. Payday is faked page by page.
/// </summary>
public class PaydayProductSyncTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class FakeProducts(params List<PaydayProduct>[] pages) : IPaydayProductsApi
    {
        public string? FailWith { get; set; }
        public List<int> PagesRequested { get; } = [];

        public Task<ApiResult<PaydayProductsResponse>> GetAllAsync(int page = 1, int perPage = 100, string orderBy = "sku", string order = "asc", string? query = null)
        {
            PagesRequested.Add(page);
            if (FailWith is not null) return Task.FromResult(ApiResult<PaydayProductsResponse>.Failure(FailWith));
            var items = page <= pages.Length ? pages[page - 1] : [];
            return Task.FromResult(ApiResult<PaydayProductsResponse>.Success(new PaydayProductsResponse
            {
                Products = items, Page = page, Pages = pages.Length, PerPage = perPage, Total = pages.Sum(p => p.Count),
            }));
        }

        public Task<ApiResult<PaydayProduct>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ApiResult<PaydayProduct>> GetBySkuAsync(string sku) => throw new NotSupportedException();
        public Task<ApiResult<PaydayProductMovementsResponse>> GetMovementsAsync(Guid id, int page = 1, int perPage = 100) => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync() => throw new NotSupportedException();
    }

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"pdsync-{Guid.NewGuid()}").Options);

    private static PaydayProduct Cable(Guid id, decimal? qty = 100) => new()
    {
        Id = id, Sku = "N1XE-5G16", Name = "Cable 5G16", Quantity = qty,
        SalesUnitPriceExcludingVAT = 1500, SalesUnitPriceIncludingVAT = 1860, VatPercentage = 24,
        SalesLedgerAccountId = Guid.NewGuid(), Tags = ["strengir"],
    };

    private static PaydayProduct Labor(Guid id) => new()
    {
        Id = id, Sku = "VINNA", Name = "Vinna klst", Quantity = null,
        SalesUnitPriceExcludingVAT = 12000, SalesUnitPriceIncludingVAT = 14880, VatPercentage = 24,
    };

    [Fact]
    public async Task FirstSync_AddsEveryPage_AndDefaultsRoleFromStockTracking()
    {
        await using var db = NewDb();
        var cable = Guid.NewGuid(); var labor = Guid.NewGuid();
        var payday = new FakeProducts([Cable(cable)], [Labor(labor)]);

        var outcome = await PaydayProductEndpoints.SyncAsync(db, payday, CompanyId, CancellationToken.None);

        outcome.Error.Should().BeNull();
        outcome.Result!.Fetched.Should().Be(2);
        outcome.Result.Added.Should().Be(2);
        payday.PagesRequested.Should().Equal(1, 2);

        var rows = await db.PaydayProducts.OrderBy(p => p.Sku).ToListAsync();
        rows.Should().HaveCount(2);
        var c = rows.Single(r => r.PaydayId == cable);
        c.Role.Should().Be(PaydayProductRole.Material);
        c.IsStockTracked.Should().BeTrue();
        c.SalePriceExVat.Should().Be(1500); c.VatPercentage.Should().Be(24); c.Tags.Should().Be("strengir");
        var l = rows.Single(r => r.PaydayId == labor);
        l.Role.Should().Be(PaydayProductRole.Unassigned);
        l.IsStockTracked.Should().BeFalse();
    }

    [Fact]
    public async Task Resync_UpdatesPaydayFields_KeepsWorkitFields_ArchivesMissing()
    {
        await using var db = NewDb();
        var cable = Guid.NewGuid(); var labor = Guid.NewGuid();
        await PaydayProductEndpoints.SyncAsync(db, new FakeProducts([Cable(cable), Labor(labor)]), CompanyId, CancellationToken.None);

        // The owner assigns roles and a unit, then Payday changes the price and drops the labor product.
        var laborRow = await db.PaydayProducts.SingleAsync(p => p.PaydayId == labor);
        laborRow.Role = PaydayProductRole.RegularHour; laborRow.Unit = "klst.";
        var cableRow = await db.PaydayProducts.SingleAsync(p => p.PaydayId == cable);
        cableRow.Unit = "m."; cableRow.Category = "Rafstrengir";
        await db.SaveChangesAsync();

        var repriced = Cable(cable, qty: 83); repriced.SalesUnitPriceExcludingVAT = 1600;
        var outcome = await PaydayProductEndpoints.SyncAsync(db, new FakeProducts([repriced]), CompanyId, CancellationToken.None);

        outcome.Result!.Should().BeEquivalentTo(new { Fetched = 1, Added = 0, Updated = 1, Archived = 1 });
        cableRow = await db.PaydayProducts.SingleAsync(p => p.PaydayId == cable);
        cableRow.SalePriceExVat.Should().Be(1600);
        cableRow.Quantity.Should().Be(83);
        cableRow.Unit.Should().Be("m."); cableRow.Category.Should().Be("Rafstrengir");   // Workit's, untouched
        laborRow = await db.PaydayProducts.SingleAsync(p => p.PaydayId == labor);
        laborRow.Archived.Should().BeTrue();                    // gone from Payday → archived, not deleted
        laborRow.Role.Should().Be(PaydayProductRole.RegularHour);
        (await db.PaydayProducts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Sync_IsScopedToTheCompany()
    {
        await using var db = NewDb();
        var other = Guid.NewGuid();
        db.PaydayProducts.Add(new PaydayProductCache { CompanyId = other, PaydayId = Guid.NewGuid(), Sku = "OTHER", Name = "Other company's" });
        await db.SaveChangesAsync();

        await PaydayProductEndpoints.SyncAsync(db, new FakeProducts([Cable(Guid.NewGuid())]), CompanyId, CancellationToken.None);

        var otherRow = await db.PaydayProducts.SingleAsync(p => p.CompanyId == other);
        otherRow.Archived.Should().BeFalse();                   // never touched by another company's sync
    }

    [Fact]
    public async Task Sync_WhenPaydayFails_ChangesNothing_AndReturnsPaydaysMessage()
    {
        await using var db = NewDb();
        await PaydayProductEndpoints.SyncAsync(db, new FakeProducts([Cable(Guid.NewGuid())]), CompanyId, CancellationToken.None);

        var outcome = await PaydayProductEndpoints.SyncAsync(db, new FakeProducts([]) { FailWith = "Client authentication failed" }, CompanyId, CancellationToken.None);

        outcome.Result.Should().BeNull();
        outcome.Error.Should().Be("Client authentication failed");
        (await db.PaydayProducts.SingleAsync()).Archived.Should().BeFalse();
    }
}
