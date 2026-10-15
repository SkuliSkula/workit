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
/// Workit keeps its own copy of every Payday invoice and the invoice pages read
/// from it. The copy has to follow Payday — new invoices, status and payment
/// changes, deleted drafts — without ever doubling up or trusting a half list.
/// </summary>
public class PaydayInvoiceSyncTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private readonly FakeInvoices payday = new();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"invoice-sync-{Guid.NewGuid()}").Options);

    private PaydayInvoiceSyncService Service(WorkitDbContext db) => new(db, payday, NullLogger<PaydayInvoiceSyncService>.Instance);

    private static readonly PaydayCustomer Customer = new() { Id = Guid.NewGuid(), Name = "Seðlabankinn", Ssn = "5609002260" };

    private static PaydayInvoice Remote(int? number, string status = "SENT", decimal amount = 12_400m, params (string Description, decimal Qty)[] lines) => new()
    {
        Id                 = Guid.NewGuid(),
        Number             = number,
        Status             = status,
        Customer           = Customer,
        InvoiceDate        = new DateTime(2026, 10, 1),
        DueDate            = new DateTime(2026, 10, 15),
        CurrencyCode       = "ISK",
        CurrencyRate       = 1,
        AmountExcludingVat = amount / 1.24m,
        AmountIncludingVat = amount,
        Created            = new DateTime(2026, 10, 1, 9, 0, 0),
        Lines = lines.Select((l, i) => new PaydayInvoiceLine
        {
            Id = Guid.NewGuid(), Position = i + 1, Description = l.Description, Quantity = l.Qty,
            UnitPriceExcludingVat = 10_000m, UnitPriceIncludingVat = 12_400m, VatPercentage = 24m,
        }).ToList(),
        Payments = [],
    };

    [Fact]
    public async Task A_sync_stores_every_invoice_with_its_lines_and_payments_across_pages()
    {
        await using var db = NewDb();
        for (var n = 1; n <= 130; n++) payday.Invoices.Add(Remote(n, lines: ("Vinna", 1m)));
        payday.Invoices[0].Payments = [new PaydayInvoicePayment { Id = Guid.NewGuid(), PaymentTypeId = Guid.NewGuid(), PaymentDate = new DateTime(2026, 10, 10), Amount = 5_000m }];

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Error.Should().BeNull();
        outcome.Result!.Added.Should().Be(130);
        (await db.Invoices.CountAsync(i => i.CompanyId == companyId)).Should().Be(130);
        (await db.InvoiceLines.CountAsync()).Should().Be(130);
        var paid = await db.InvoicePayments.SingleAsync();
        paid.Amount.Should().Be(5_000m);
        paid.PaymentDate.Kind.Should().Be(DateTimeKind.Utc, "Postgres timestamptz refuses unspecified dates");
        (await db.Invoices.FirstAsync()).Source.Should().Be(DataSource.Payday);
    }

    [Fact]
    public async Task A_second_sync_follows_Payday_status_payments_and_lines_without_doubling_up()
    {
        await using var db = NewDb();
        var invoice = Remote(4211, lines: [("Vinna", 8m), ("Efni", 3m)]);
        payday.Invoices.Add(invoice);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        invoice.Status   = "PAID";
        invoice.PaidDate = new DateTime(2026, 10, 12);
        invoice.Payments = [new PaydayInvoicePayment { Id = Guid.NewGuid(), PaymentDate = new DateTime(2026, 10, 12), Amount = 12_400m }];
        invoice.Lines!.RemoveAt(1);
        invoice.Lines[0].Quantity = 9m;

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Updated.Should().Be(1);
        outcome.Result.Added.Should().Be(0);
        var stored = await db.Invoices.Include(i => i.Lines).Include(i => i.Payments).SingleAsync();
        stored.Status.Should().Be("PAID");
        stored.PaidDate.Should().Be(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        stored.Lines.Should().ContainSingle().Which.Quantity.Should().Be(9m);
        stored.Payments.Should().ContainSingle().Which.Amount.Should().Be(12_400m);
    }

    [Fact]
    public async Task A_draft_deleted_in_Payday_leaves_Workit_too()
    {
        await using var db = NewDb();
        var kept = Remote(4211);
        var gone = SeedStored(db, number: null, status: "DRAFT", syncedAgo: TimeSpan.FromHours(1));
        payday.Invoices.Add(kept);

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Removed.Should().Be(1);
        (await db.Invoices.AnyAsync(i => i.PaydayId == gone)).Should().BeFalse();
        (await db.Invoices.AnyAsync(i => i.PaydayId == kept.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_is_removed_when_Payday_lists_fewer_invoices_than_it_says_it_has()
    {
        await using var db = NewDb();
        var stored = SeedStored(db, number: 4100, status: "SENT", syncedAgo: TimeSpan.FromHours(1));
        payday.Invoices.Add(Remote(4211));
        payday.ReportedTotal = 2;

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Removed.Should().Be(0);
        (await db.Invoices.AnyAsync(i => i.PaydayId == stored)).Should().BeTrue();
    }

    [Fact]
    public async Task An_invoice_stored_minutes_ago_is_not_removed_just_because_Payday_has_not_listed_it_yet()
    {
        await using var db = NewDb();
        var fresh = SeedStored(db, number: null, status: "DRAFT", syncedAgo: TimeSpan.FromMinutes(1));

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Removed.Should().Be(0);
        (await db.Invoices.AnyAsync(i => i.PaydayId == fresh)).Should().BeTrue();
    }

    [Fact]
    public async Task A_lagging_list_does_not_undo_a_change_Workit_just_stored()
    {
        await using var db = NewDb();
        var invoice = Remote(4211, status: "SENT");
        payday.Invoices.Add(invoice);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        // Marked paid from Workit: Payday's write response is stored straight away…
        var paid = Remote(4211, status: "PAID");
        paid.Id = invoice.Id;
        await Service(db).SaveAsync(companyId, paid, CancellationToken.None);

        // …but Payday's list still says SENT for a minute.
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        (await db.Invoices.SingleAsync()).Status.Should().Be("PAID");
    }

    [Fact]
    public async Task A_draft_deleted_from_Workit_is_not_brought_back_by_a_lagging_list()
    {
        await using var db = NewDb();
        var draft = Remote(null, status: "DRAFT");
        payday.Invoices.Add(draft);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        await Service(db).RemoveAsync(companyId, draft.Id, CancellationToken.None);
        await Service(db).SyncAsync(companyId, CancellationToken.None);   // Payday still lists it

        (await db.Invoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task When_Payday_cannot_be_reached_the_stored_copy_is_left_as_it_is()
    {
        await using var db = NewDb();
        var stored = SeedStored(db, number: 4100, status: "SENT", syncedAgo: TimeSpan.FromHours(1));
        payday.Fail = true;

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Error.Should().NotBeNull();
        (await db.Invoices.SingleAsync()).PaydayId.Should().Be(stored);
    }

    [Fact]
    public async Task Another_companys_invoices_are_never_touched()
    {
        await using var db = NewDb();
        var other = Guid.NewGuid();
        db.Invoices.Add(new Invoice { CompanyId = other, PaydayId = Guid.NewGuid(), Source = DataSource.Payday, Status = "DRAFT", SyncedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        await Service(db).SyncAsync(companyId, CancellationToken.None);

        (await db.Invoices.CountAsync(i => i.CompanyId == other)).Should().Be(1);
    }

    [Fact]
    public async Task The_stored_list_answers_with_Paydays_filters_order_and_totals()
    {
        await using var db = NewDb();
        var draft    = Remote(null, status: "DRAFT", amount: 1_000m);
        var sent     = Remote(4212, status: "SENT", amount: 10_000m);
        var paid     = Remote(4211, status: "PAID", amount: 20_000m);
        var other    = Remote(4210, status: "SENT", amount: 99_000m);
        other.Customer = new PaydayCustomer { Id = Guid.NewGuid(), Name = "Annar" };
        sent.Payments  = [new PaydayInvoicePayment { Id = Guid.NewGuid(), PaymentDate = new DateTime(2026, 10, 5), Amount = 4_000m }];
        payday.Invoices.AddRange([draft, sent, paid, other]);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        var all = await PaydayInvoiceStore.ListAsync(db, companyId, new PaydayInvoiceStore.ListQuery(PerPage: 10), CancellationToken.None);
        all.Invoices.Select(i => i.Number).Should().Equal(null, 4212, 4211, 4210);
        all.Invoices[1].Lines.Should().NotBeNull();
        all.Invoices[1].Customer!.Name.Should().Be("Seðlabankinn");

        var open = await PaydayInvoiceStore.ListAsync(db, companyId,
            new PaydayInvoiceStore.ListQuery(PerPage: 1, Include: null, ExcludeStatus: "DRAFT,CANCELLED,PAID", CustomerId: Customer.Id), CancellationToken.None);
        open.Total.Should().Be(1);
        open.Summary!.UnpaidAmount.Should().Be(6_000m, "the 10 000 invoice has 4 000 paid on it");
        open.Invoices.Single().Lines.Should().BeNull("lines were not asked for");

        var customer = await PaydayInvoiceStore.ListAsync(db, companyId, new PaydayInvoiceStore.ListQuery(CustomerId: Customer.Id), CancellationToken.None);
        customer.Summary!.PaidAmount.Should().Be(24_000m);
        customer.Total.Should().Be(3);

        var oldestFirst = await PaydayInvoiceStore.ListAsync(db, companyId,
            new PaydayInvoiceStore.ListQuery(Order: "asc", ExcludeStatus: "DELETED"), CancellationToken.None);
        oldestFirst.Invoices.Select(i => i.Number).Should().Equal(4210, 4211, 4212, null);

        var october = await PaydayInvoiceStore.ListAsync(db, companyId,
            new PaydayInvoiceStore.ListQuery(DateFrom: "2026-10-01", DateTo: "2026-10-01"), CancellationToken.None);
        october.Total.Should().Be(4, "the end date takes in the whole day");
        var november = await PaydayInvoiceStore.ListAsync(db, companyId,
            new PaydayInvoiceStore.ListQuery(DateFrom: "2026-11-01"), CancellationToken.None);
        november.Total.Should().Be(0);
    }

    private Guid SeedStored(WorkitDbContext db, int? number, string status, TimeSpan syncedAgo)
    {
        var paydayId = Guid.NewGuid();
        db.Invoices.Add(new Invoice
        {
            CompanyId = companyId, PaydayId = paydayId, Source = DataSource.Payday, Number = number, Status = status,
            SyncedAt = DateTimeOffset.UtcNow - syncedAgo, CreatedAt = DateTimeOffset.UtcNow - syncedAgo,
        });
        db.SaveChanges();
        return paydayId;
    }

    private sealed class FakeInvoices : IPaydayInvoicesApi
    {
        public List<PaydayInvoice> Invoices { get; } = [];
        public int? ReportedTotal { get; set; }
        public bool Fail { get; set; }

        public Task<ApiResult<PaydayInvoicesResponse>> GetAllAsync(int page = 1, int perPage = 25, string? include = "lines",
            string? dateFrom = null, string? dateTo = null, string? excludeStatus = null, Guid? customerId = null,
            string? query = null, string order = "desc", string orderBy = "number")
        {
            if (Fail) return Task.FromResult(ApiResult<PaydayInvoicesResponse>.Failure("Payday is down."));
            var pageItems = Invoices.Skip((page - 1) * perPage).Take(perPage).ToList();
            return Task.FromResult(ApiResult<PaydayInvoicesResponse>.Success(new PaydayInvoicesResponse
            {
                Invoices = pageItems,
                Page     = page,
                PerPage  = perPage,
                Total    = ReportedTotal ?? Invoices.Count,
                Pages    = Math.Max(1, (int)Math.Ceiling(Invoices.Count / (double)perPage)),
            }));
        }

        public Task<ApiResult<PaydayInvoice>> GetByIdAsync(string invoiceId, string? include = "lines,payments") => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoice>> CreateAsync(CreateInvoiceRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoice>> UpdateAsync(string invoiceId, UpdateInvoiceRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string invoiceId) => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetPdfAsync(string invoiceId) => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetAttachmentAsync(string invoiceId) => throw new NotSupportedException();
    }
}
