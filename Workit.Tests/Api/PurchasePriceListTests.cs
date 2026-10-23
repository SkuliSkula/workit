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
/// Goods bought for resale keep the Payday price list up to date: a higher Ein.verð
/// raises the product's price, an item not in the list is created, nothing is ever
/// lowered, and once on it only acts on purchases entered after it was switched on.
/// </summary>
public class PurchasePriceListTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private static readonly Guid GoodsAccount = Guid.NewGuid();
    private static readonly Guid ToolsAccount = Guid.NewGuid();
    private static readonly Guid Ledger24 = Guid.NewGuid();
    private static readonly Guid Ledger11 = Guid.NewGuid();
    private readonly FakeProducts payday = new();

    private async Task<WorkitDbContext> NewDbAsync()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"price-list-{Guid.NewGuid()}").Options);
        db.Companies.Add(new Company { Id = companyId, Name = "ÓS" });
        await db.SaveChangesAsync();
        await PaydaySnapshotStore.SaveAsync(db, companyId, PaydaySnapshotStore.ExpenseAccounts, new List<PaydayExpenseAccount>
        {
            new() { Id = GoodsAccount, Name = "Vörukaup innanlands" },
            new() { Id = ToolsAccount, Name = "Verkfæri og áhöld" },
        }, CancellationToken.None);
        return db;
    }

    private PurchasePriceListService Service(WorkitDbContext db) =>
        new(db, payday, new PaydayProductSyncService(db, payday, NullLogger<PaydayProductSyncService>.Instance), NullLogger<PurchasePriceListService>.Instance);

    /// <summary>An existing price-list product, in Payday and in Workit's cache.</summary>
    private void InPriceList(WorkitDbContext db, string sku, decimal price, bool archived = false)
    {
        var product = new PaydayProduct
        {
            Id = Guid.NewGuid(), Sku = sku, Name = "Product " + sku, SalesUnitPriceExcludingVAT = price, VatPercentage = 24,
            SalesLedgerAccountId = Ledger24, Tags = ["rafmagn"], Archived = archived,
        };
        payday.Products.Add(product);
        db.PaydayProducts.Add(new PaydayProductCache
        {
            CompanyId = companyId, PaydayId = product.Id, Sku = sku, Name = product.Name, SalePriceExVat = price, VatPercentage = 24,
            Archived = archived, Role = PaydayProductRole.Material,
        });
        db.SaveChanges();
    }

    /// <summary>A purchase with one line; the total is what the vendor charged (before VAT, after the discount).</summary>
    private void Bought(WorkitDbContext db, string description, decimal einVerd, decimal discount = 0m, decimal quantity = 1m,
        Guid? account = null, DateTime? on = null, DateTimeOffset? entered = null, decimal vat = 24m, bool einVerdHasVat = false)
    {
        var unit = einVerdHasVat ? einVerd * (1 + vat / 100) : einVerd;
        var expense = new Expense
        {
            CompanyId = companyId, PaydayId = Guid.NewGuid(), Source = DataSource.Payday, CreditorName = "Húsasmiðjan ehf.", Status = "PAID",
            Date = on ?? new DateTime(2026, 10, 2), CreatedAt = entered ?? new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
            AmountExcludingVat = Math.Round(quantity * einVerd * (1 - discount / 100), 2),
        };
        expense.Lines.Add(new ExpenseLine
        {
            ExpenseId = expense.Id, CompanyId = companyId, Description = description, Quantity = quantity,
            UnitPriceExcludingVat = unit, DiscountPercentage = discount, VatPercentage = vat, AccountId = account ?? GoodsAccount,
        });
        db.Expenses.Add(expense);
        db.SaveChanges();
    }

    [Theory]
    [InlineData("6268002 - PROFILED BORÐI 230V 25m 3000K", "6268002", "PROFILED BORÐI 230V 25m 3000K")]
    [InlineData("SG 614393 - Duett Svart E27 10w LED", "SG 614393", "Duett Svart E27 10w LED")]
    [InlineData("221_415 - Víratengi smellt 5x 0,2-4q (25)", "221_415", "Víratengi smellt 5x 0,2-4q (25)")]
    [InlineData("BLA-360210309900-M - T-BOLUR SVARTUR - M", "BLA-360210309900-M", "T-BOLUR SVARTUR - M")]
    public void The_item_number_is_what_comes_before_the_first_dash(string description, string sku, string name)
    {
        PurchasePriceListService.ItemOf(description).Should().Be((sku, name));
    }

    [Theory]
    [InlineData("Raflagnaefni")]
    [InlineData("Alhef fur/gre 21x58 húsðurr - 420")]
    [InlineData("Akstur - Reykjavík")]
    [InlineData(" - nothing before")]
    [InlineData(null)]
    public void A_line_without_an_item_number_is_not_an_item(string? description)
    {
        PurchasePriceListService.ItemOf(description).Should().BeNull();
    }

    [Fact]
    public async Task A_higher_Ein_verd_raises_the_price_and_a_new_item_is_added()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI 230V 25m 3000K", 70395.16m, discount: 45m);
        Bought(db, "6241620 - LED LAB LUMINES Profíll U-FORM 2m", 6780.65m, discount: 45m, quantity: 10);

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);

        plan.Raises.Should().ContainSingle().Which.Should().Match<PriceRaise>(r => r.Sku == "6268002" && r.CurrentPriceExVat == 65000m && r.NewPriceExVat == 70395m);
        plan.Additions.Should().ContainSingle().Which.Should().Match<PriceListAddition>(a => a.Sku == "6241620" && a.PriceExVat == 6781m && a.VatPercentage == 24m);
    }

    [Fact]
    public async Task A_price_is_never_lowered()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 74900m);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m);

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);

        plan.Raises.Should().BeEmpty();
        plan.Additions.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_goods_for_resale_with_an_item_number_count()
    {
        await using var db = await NewDbAsync();
        Bought(db, "5027110 - BOR SNIGILBOR 10MM 450MM", 3581m, account: ToolsAccount);
        Bought(db, "Raflagnaefni", 12000m);
        Bought(db, "6087325 - GÚMMÍSTR.H07RN-F 2X1,5", -319m);   // a return

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);

        plan.Raises.Should().BeEmpty();
        plan.Additions.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_item_is_judged_by_its_latest_purchase()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI", 72000m, on: new DateTime(2026, 9, 1));
        Bought(db, "6268002 - PROFILED BORÐI", 64000m, on: new DateTime(2026, 10, 1));

        (await Service(db).PlanAsync(companyId, null, CancellationToken.None)).Raises.Should().BeEmpty("the newest purchase is cheaper than the list");
    }

    [Fact]
    public async Task An_Ein_verd_printed_with_VAT_is_read_without_it()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m, discount: 45m, einVerdHasVat: true);

        (await Service(db).PlanAsync(companyId, null, CancellationToken.None)).Raises.Single().NewPriceExVat.Should().Be(70395m);
    }

    [Fact]
    public async Task An_archived_product_is_reported_and_never_recreated()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m, archived: true);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m);

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);

        plan.Additions.Should().BeEmpty();
        plan.ArchivedSkus.Should().Equal("6268002");
    }

    [Fact]
    public async Task Applying_raises_without_touching_stock_creates_materials_and_switches_it_on()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI", 70395.16m, discount: 45m);
        Bought(db, "6241620 - LED LAB LUMINES Profíll", 6780.65m, discount: 45m, quantity: 10);
        Bought(db, "1001 - Bók", 3000m, vat: 11m);
        var service = Service(db);

        var result = await service.ApplyAsync(companyId, await service.PlanAsync(companyId, null, CancellationToken.None), CancellationToken.None);

        result.Should().Match<PriceListApplyResult>(r => r.Raised == 1 && r.Created == 2 && r.Failed.Count == 0);
        var update = payday.Updates.Single();
        update.Request.SalesUnitPriceExcludingVAT.Should().Be(70395m);
        update.Request.Tags.Should().Equal(new[] { "rafmagn" }, "the product's own values are kept");
        update.Request.SalesLedgerAccountId.Should().Be(Ledger24);
        payday.Creates.Should().HaveCount(2);
        payday.Creates.Should().OnlyContain(c => c.Quantity == null, "no stock tracking");
        payday.Creates.Single(c => c.Sku == "1001").SalesLedgerAccountId.Should().Be(Ledger11, "11 % VAT goes to the reduced-VAT sales account");
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "6241620")).Role.Should().Be(PaydayProductRole.Material);
        (await db.PaydayProducts.SingleAsync(p => p.Sku == "6268002")).SalePriceExVat.Should().Be(70395m);
        (await db.Companies.SingleAsync()).PriceListFromPurchasesSince.Should().NotBeNull();
    }

    [Fact]
    public async Task Once_on_only_purchases_entered_afterwards_change_the_list()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        var company = await db.Companies.SingleAsync();
        company.PriceListFromPurchasesSince = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        Bought(db, "6268002 - PROFILED BORÐI", 70395m, entered: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));   // before: ignored
        Bought(db, "7777001 - Nýtt", 5000m, entered: new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

        var result = await Service(db).SyncAsync(companyId, CancellationToken.None);

        result!.Raised.Should().Be(0);
        result.Created.Should().Be(1);
        payday.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task While_off_the_background_run_does_nothing()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m);

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Should().BeNull();
        payday.Updates.Should().BeEmpty();
        payday.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task A_price_Payday_already_raised_meanwhile_is_not_written_again()
    {
        await using var db = await NewDbAsync();
        InPriceList(db, "6268002", 65000m);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m);
        var service = Service(db);
        var plan = await service.PlanAsync(companyId, null, CancellationToken.None);
        payday.Products.Single().SalesUnitPriceExcludingVAT = 72000m;   // someone raised it in Payday

        var result = await service.ApplyAsync(companyId, plan, CancellationToken.None);

        result.Raised.Should().Be(0);
        payday.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_goods_account_it_says_so_and_plans_nothing()
    {
        await using var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"price-list-{Guid.NewGuid()}").Options);
        Bought(db, "6268002 - PROFILED BORÐI", 70395m);

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);

        plan.Problem.Should().Contain("Vörukaup innanlands");
        plan.Additions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delivery_or_rounding_line_is_flagged_and_left_out_of_the_automatic_run()
    {
        await using var db = await NewDbAsync();
        Bought(db, "000002 - AKSTUR REYKJAVÍK", 4910m);
        Bought(db, "90004 - Auramunur sléttun", 3m);
        Bought(db, "7777001 - Varrofi C16", 1450m);

        var plan = await Service(db).PlanAsync(companyId, null, CancellationToken.None);
        plan.Additions.Where(a => a.LooksLikeCharge).Select(a => a.Sku).Should().BeEquivalentTo(["000002", "90004"]);

        var company = await db.Companies.SingleAsync();
        company.PriceListFromPurchasesSince = DateTimeOffset.MinValue;
        await db.SaveChangesAsync();
        var result = await Service(db).SyncAsync(companyId, CancellationToken.None);

        result!.Created.Should().Be(1);
        payday.Creates.Should().ContainSingle().Which.Sku.Should().Be("7777001");
    }

    [Fact]
    public async Task An_item_the_owner_unticked_is_never_suggested_again()
    {
        await using var db = await NewDbAsync();
        Bought(db, "000002 - AKSTUR REYKJAVÍK", 4910m);
        Bought(db, "7777001 - Varrofi C16", 1450m);
        var service = Service(db);

        await service.IgnoreAsync(companyId, ["000002"], CancellationToken.None);
        var plan = await service.PlanAsync(companyId, null, CancellationToken.None);

        plan.Additions.Select(a => a.Sku).Should().Equal("7777001");
        plan.IgnoredCount.Should().Be(1);
        (await db.Companies.SingleAsync()).PriceListIgnoredSkus.Should().Be("000002");
    }

    internal sealed class FakeProducts : IPaydayProductsApi
    {
        public List<PaydayProduct> Products { get; } = [];
        public List<CreateProductRequest> Creates { get; } = [];
        public List<(Guid Id, UpdateProductRequest Request)> Updates { get; } = [];

        public Task<ApiResult<PaydayProductsResponse>> GetAllAsync(int page = 1, int perPage = 100, string orderBy = "sku", string order = "asc", string? query = null) =>
            Task.FromResult(ApiResult<PaydayProductsResponse>.Success(new PaydayProductsResponse { Products = Products.ToList(), Page = 1, Pages = 1, PerPage = perPage, Total = Products.Count }));

        public Task<ApiResult<PaydayProduct>> GetByIdAsync(Guid id) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Id == id) is { } p ? ApiResult<PaydayProduct>.Success(p) : ApiResult<PaydayProduct>.Failure("Not found", 404));

        public Task<ApiResult<PaydayProduct>> CreateAsync(CreateProductRequest request)
        {
            Creates.Add(request);
            var product = new PaydayProduct
            {
                Id = Guid.NewGuid(), Sku = request.Sku, Name = request.Name, SalesUnitPriceExcludingVAT = request.SalesUnitPriceExcludingVAT,
                VatPercentage = request.VatPercentage, SalesLedgerAccountId = request.SalesLedgerAccountId, Quantity = request.Quantity, Tags = request.Tags,
            };
            Products.Add(product);
            return Task.FromResult(ApiResult<PaydayProduct>.Success(product));
        }

        public Task<ApiResult<PaydayProduct>> UpdateAsync(Guid id, UpdateProductRequest request)
        {
            Updates.Add((id, request));
            var product = Products.Single(p => p.Id == id);
            product.SalesUnitPriceExcludingVAT = request.SalesUnitPriceExcludingVAT;
            return Task.FromResult(ApiResult<PaydayProduct>.Success(product));
        }

        public Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync() =>
            Task.FromResult(ApiResult<List<PaydayLedgerAccount>>.Success(
            [
                new() { Id = Ledger24, Code = "3100", Name = "Sala 24%", DefaultVatPercentType = "StandardVAT" },
                new() { Id = Ledger11, Code = "3110", Name = "Sala 11%", DefaultVatPercentType = "ReducedVAT" },
            ]));

        public Task<ApiResult<PaydayProduct>> GetBySkuAsync(string sku) => throw new NotSupportedException();
        public Task<ApiResult<PaydayProductMovementsResponse>> GetMovementsAsync(Guid id, int page = 1, int perPage = 100) => throw new NotSupportedException();
    }
}
