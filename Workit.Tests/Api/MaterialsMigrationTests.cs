using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Switching a company's materials over to Payday products: match by SKU,
/// create what is missing with Workit's stock as the opening balance, refuse
/// what cannot map, then mirror Payday back into Materials.
/// </summary>
public class MaterialsMigrationTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Ledger = Guid.NewGuid();

    private sealed class FakePayday : IPaydayProductsApi
    {
        public List<PaydayProduct> Products { get; } = [];
        public List<CreateProductRequest> Created { get; } = [];

        public Task<ApiResult<PaydayProductsResponse>> GetAllAsync(int page = 1, int perPage = 100, string orderBy = "sku", string order = "asc", string? query = null) =>
            Task.FromResult(ApiResult<PaydayProductsResponse>.Success(new PaydayProductsResponse { Products = page == 1 ? Products : [], Page = page, Pages = 1, PerPage = perPage, Total = Products.Count }));

        public Task<ApiResult<PaydayProduct>> CreateAsync(CreateProductRequest request)
        {
            Created.Add(request);
            var p = new PaydayProduct { Id = Guid.NewGuid(), Sku = request.Sku, Name = request.Name, Quantity = request.Quantity, SalesUnitPriceExcludingVAT = request.SalesUnitPriceExcludingVAT, VatPercentage = request.VatPercentage, SalesLedgerAccountId = request.SalesLedgerAccountId };
            Products.Add(p);
            return Task.FromResult(ApiResult<PaydayProduct>.Success(p));
        }

        public Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync() =>
            Task.FromResult(ApiResult<List<PaydayLedgerAccount>>.Success([new PaydayLedgerAccount { Id = Ledger, Code = "1100", Name = "Sala með 24% VSK", DefaultVatPercentType = "StandardVAT" }]));

        public Task<ApiResult<PaydayProduct>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ApiResult<PaydayProduct>> GetBySkuAsync(string sku) => throw new NotSupportedException();
        public Task<ApiResult<PaydayProductMovementsResponse>> GetMovementsAsync(Guid id, int page = 1, int perPage = 100) => throw new NotSupportedException();
    }

    private static (WorkitDbContext db, FakePayday payday, MaterialsMigrationService migration) Setup()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"mig-{Guid.NewGuid()}").Options);
        db.Companies.Add(new Company { Id = CompanyId, Name = "Test" });
        db.SaveChanges();
        var payday = new FakePayday();
        var sync = new PaydayProductSyncService(db, payday, NullLogger<PaydayProductSyncService>.Instance);
        return (db, payday, new MaterialsMigrationService(db, payday, sync, NullLogger<MaterialsMigrationService>.Instance));
    }

    private static Material Mat(string name, string code, decimal qty = 10, decimal price = 100) =>
        new() { CompanyId = CompanyId, Name = name, ProductCode = code, Category = "Strengir", Unit = "m.", Quantity = qty, UnitPrice = price, VatRate = 24, PurchasePrice = 60 };

    [Fact]
    public async Task DryRun_PlansLinkCreateAndConflicts_WithoutChangingAnything()
    {
        var (db, payday, migration) = Setup();
        var existing = Guid.NewGuid();
        payday.Products.Add(new PaydayProduct { Id = existing, Sku = "N1XE5G16", Name = "Cable", Quantity = 300, SalesUnitPriceExcludingVAT = 1600, VatPercentage = 24 });
        db.Materials.AddRange(Mat("Cable", "N1XE5G16"), Mat("Clamp", "FK016"), Mat("No code", ""), Mat("Dup A", "X1"), Mat("Dup B", "x1"));
        await db.SaveChangesAsync();

        var result = await migration.RunAsync(CompanyId, dryRun: true, CancellationToken.None);

        result.DryRun.Should().BeTrue();
        result.Linked.Should().Be(1); result.Created.Should().Be(1); result.Conflicts.Should().Be(3);
        result.Rows.Single(r => r.Name == "Cable").Action.Should().Be(MaterialsMigrationAction.Link);
        result.Rows.Single(r => r.Name == "Clamp").Action.Should().Be(MaterialsMigrationAction.Create);
        result.Rows.Single(r => r.Name == "No code").Message.Should().Contain("No product code");
        result.Rows.Where(r => r.Name.StartsWith("Dup")).Should().OnlyContain(r => r.Action == MaterialsMigrationAction.Conflict);
        result.SwitchedOver.Should().BeFalse();
        payday.Created.Should().BeEmpty();
        (await db.Materials.CountAsync(m => m.PaydayProductId != null)).Should().Be(0);
        (await db.Companies.SingleAsync()).MaterialsManagedInPayday.Should().BeFalse();
    }

    [Fact]
    public async Task Run_LinksBySku_CreatesMissing_SwitchesOver_AndMirrorsStockFromPayday()
    {
        var (db, payday, migration) = Setup();
        var existing = Guid.NewGuid();
        payday.Products.Add(new PaydayProduct { Id = existing, Sku = "N1XE5G16", Name = "Cable in Payday", Quantity = 300, SalesUnitPriceExcludingVAT = 1600, VatPercentage = 24, SalesLedgerAccountId = Ledger });
        db.Materials.AddRange(Mat("Cable", "N1XE5G16", qty: 320, price: 1480), Mat("Clamp", "FK016", qty: 95, price: 480));
        await db.SaveChangesAsync();

        var result = await migration.RunAsync(CompanyId, dryRun: false, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Linked.Should().Be(1); result.Created.Should().Be(1); result.Conflicts.Should().Be(0);
        result.SwitchedOver.Should().BeTrue();

        // The clamp was created in Payday with Workit's stock as the opening balance and the 24 % sales account.
        var created = payday.Created.Single();
        created.Sku.Should().Be("FK016"); created.Quantity.Should().Be(95); created.SalesUnitPriceExcludingVAT.Should().Be(480);
        created.SalesLedgerAccountId.Should().Be(Ledger); created.PurchaseUnitPriceExcludingVAT.Should().Be(60);

        (await db.Companies.SingleAsync()).MaterialsManagedInPayday.Should().BeTrue();

        // After the mirror, Payday's numbers win: the cable shows 300 in stock at 1 600, its Payday name, and keeps Workit's unit.
        var cable = await db.Materials.SingleAsync(m => m.ProductCode == "N1XE5G16");
        cable.PaydayProductId.Should().Be(existing);
        cable.Quantity.Should().Be(300); cable.UnitPrice.Should().Be(1600); cable.Name.Should().Be("Cable in Payday"); cable.Unit.Should().Be("m.");
        (await db.PaydayProducts.SingleAsync(p => p.PaydayId == existing)).Role.Should().Be(PaydayProductRole.Material);
        (await db.Materials.SingleAsync(m => m.ProductCode == "FK016")).PaydayProductId.Should().NotBeNull();
    }

    [Fact]
    public async Task Run_WithConflicts_LinksWhatItCan_ButDoesNotSwitchOver()
    {
        var (db, payday, migration) = Setup();
        payday.Products.Add(new PaydayProduct { Id = Guid.NewGuid(), Sku = "A1", Name = "A", Quantity = 1 });
        db.Materials.AddRange(Mat("A", "A1"), Mat("Nameless", ""));
        await db.SaveChangesAsync();

        var result = await migration.RunAsync(CompanyId, dryRun: false, CancellationToken.None);

        result.Linked.Should().Be(1); result.Conflicts.Should().Be(1); result.SwitchedOver.Should().BeFalse();
        (await db.Materials.SingleAsync(m => m.ProductCode == "A1")).PaydayProductId.Should().NotBeNull();
        (await db.Companies.SingleAsync()).MaterialsManagedInPayday.Should().BeFalse();
    }

    [Fact]
    public async Task Mirror_AddsNewMaterialProducts_AndDeactivatesOnesThatLostTheRole()
    {
        var (db, payday, _) = Setup();
        var company = await db.Companies.SingleAsync(); company.MaterialsManagedInPayday = true;
        var keep = Guid.NewGuid(); var lost = Guid.NewGuid(); var fresh = Guid.NewGuid();
        db.Materials.AddRange(
            new Material { CompanyId = CompanyId, Name = "Keep", ProductCode = "K", PaydayProductId = keep, Unit = "stk." },
            new Material { CompanyId = CompanyId, Name = "Lost", ProductCode = "L", PaydayProductId = lost, Unit = "stk." });
        await db.SaveChangesAsync();
        payday.Products.AddRange(
            new PaydayProduct { Id = keep, Sku = "K", Name = "Keep", Quantity = 7, SalesUnitPriceExcludingVAT = 10, VatPercentage = 24 },
            new PaydayProduct { Id = lost, Sku = "L", Name = "Lost", Quantity = null },                 // service now → not a material
            new PaydayProduct { Id = fresh, Sku = "F", Name = "Fresh in Payday", Quantity = 40, SalesUnitPriceExcludingVAT = 5, VatPercentage = 11 });

        var sync = new PaydayProductSyncService(db, payday, NullLogger<PaydayProductSyncService>.Instance);
        await sync.SyncAsync(CompanyId, CancellationToken.None);

        var materials = await db.Materials.ToListAsync();
        materials.Single(m => m.ProductCode == "K").Quantity.Should().Be(7);
        materials.Single(m => m.ProductCode == "L").IsActive.Should().BeFalse();       // role gone → inactive, not deleted
        var f = materials.Single(m => m.ProductCode == "F");
        f.Name.Should().Be("Fresh in Payday"); f.Quantity.Should().Be(40); f.VatRate.Should().Be(11); f.IsActive.Should().BeTrue();
    }
}
