using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// A draft marks its hours, materials and expense quantities invoiced the moment
/// it is created. When it is deleted or cancelled — in Workit or inside Payday —
/// they have to come back, and only when Payday says so plainly.
/// </summary>
public class InvoiceReleaseTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"release-{Guid.NewGuid()}").Options);

    /// <summary>One draft holding an hour entry, a material usage and an expense billing.</summary>
    private static Guid SeedDraft(WorkitDbContext db, int? number = null)
    {
        var draft = Guid.NewGuid();
        db.TimeEntries.Add(new TimeEntry
        {
            CompanyId = CompanyId, EmployeeId = Guid.NewGuid(), JobId = Guid.NewGuid(),
            WorkDate = new DateOnly(2026, 9, 1), Hours = 8,
            IsInvoiced = true, InvoicedAt = DateTimeOffset.UtcNow, PaydayInvoiceNumber = number, PaydayInvoiceId = draft,
        });
        db.MaterialUsages.Add(new MaterialUsage
        {
            CompanyId = CompanyId, MaterialId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Quantity = 3,
            IsInvoiced = true, InvoicedAt = DateTimeOffset.UtcNow, PaydayInvoiceNumber = number, PaydayInvoiceId = draft,
        });
        db.ExpenseLineBillings.Add(new ExpenseLineBilling
        {
            CompanyId = CompanyId, ExpenseLineId = Guid.NewGuid(), Quantity = 2, SalePriceExcludingVat = 1074,
            PaydayInvoiceNumber = number, PaydayInvoiceId = draft,
        });
        db.SaveChanges();
        return draft;
    }

    [Fact]
    public async Task Releasing_a_draft_hands_back_its_hours_materials_and_expense_quantities()
    {
        await using var db = NewDb();
        var draft = SeedDraft(db);

        var released = await InvoiceRelease.ReleaseAsync(db, CompanyId, draft, CancellationToken.None);

        released.Should().Be(new InvoiceRelease.Released(1, 1, 1));
        var entry = await db.TimeEntries.SingleAsync();
        entry.IsInvoiced.Should().BeFalse();
        entry.InvoicedAt.Should().BeNull();
        entry.PaydayInvoiceId.Should().BeNull();
        (await db.MaterialUsages.SingleAsync()).IsInvoiced.Should().BeFalse();
        (await db.ExpenseLineBillings.CountAsync()).Should().Be(0, "removing the billing puts the quantity back on the line");
    }

    [Fact]
    public async Task Releasing_one_draft_leaves_every_other_invoice_alone()
    {
        await using var db = NewDb();
        var deleted = SeedDraft(db);
        var kept    = SeedDraft(db, number: 4211);

        await InvoiceRelease.ReleaseAsync(db, CompanyId, deleted, CancellationToken.None);

        (await db.TimeEntries.CountAsync(e => e.IsInvoiced && e.PaydayInvoiceId == kept)).Should().Be(1);
        (await db.ExpenseLineBillings.CountAsync(b => b.PaydayInvoiceId == kept)).Should().Be(1);
    }

    [Fact]
    public async Task Another_company_cannot_release_your_draft()
    {
        await using var db = NewDb();
        var draft = SeedDraft(db);

        (await InvoiceRelease.ReleaseAsync(db, Guid.NewGuid(), draft, CancellationToken.None)).Total.Should().Be(0);
        (await db.TimeEntries.SingleAsync()).IsInvoiced.Should().BeTrue();
    }

    [Fact]
    public async Task Issuing_a_draft_fills_in_its_number_without_touching_anything_else()
    {
        await using var db = NewDb();
        var draft = SeedDraft(db);

        (await InvoiceRelease.BackfillNumberAsync(db, CompanyId, draft, 4212, CancellationToken.None)).Should().Be(3);

        var entry = await db.TimeEntries.SingleAsync();
        entry.PaydayInvoiceNumber.Should().Be(4212);
        entry.IsInvoiced.Should().BeTrue();
        (await db.ExpenseLineBillings.SingleAsync()).PaydayInvoiceNumber.Should().Be(4212);
    }

    [Theory]
    [InlineData("CANCELLED", true)]
    [InlineData("DELETED",   true)]
    [InlineData("cancelled", true)]
    [InlineData("DRAFT",     false)]
    [InlineData("SENT",      false)]
    [InlineData("PAID",      false)]
    [InlineData("CREDIT",    false)]
    [InlineData(null,        false)]
    public void Only_an_invoice_that_bills_nothing_releases_its_records(string? status, bool releases) =>
        InvoiceRelease.ReleasesRecords(status).Should().Be(releases);

    // ── Reconciling against Payday ───────────────────────────────────────────

    private sealed class FakePayday(Func<string, ApiResult<PaydayInvoice>> getById) : IPaydayInvoicesApi
    {
        public Task<ApiResult<PaydayInvoice>> GetByIdAsync(string invoiceId, string? include = "lines,payments") =>
            Task.FromResult(getById(invoiceId));

        public Task<ApiResult<PaydayInvoicesResponse>> GetAllAsync(
            int page = 1, int perPage = 25, string? include = "lines", string? dateFrom = null, string? dateTo = null,
            string? excludeStatus = null, Guid? customerId = null, string? query = null,
            string order = "desc", string orderBy = "number") => throw new NotSupportedException();

        public Task<ApiResult<PaydayInvoice>> CreateAsync(CreateInvoiceRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoice>> UpdateAsync(string invoiceId, UpdateInvoiceRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string invoiceId) => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetPdfAsync(string invoiceId) => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetAttachmentAsync(string invoiceId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_draft_deleted_inside_Payday_is_released_when_Payday_says_404()
    {
        await using var db = NewDb();
        SeedDraft(db);
        var payday = new FakePayday(_ => ApiResult<PaydayInvoice>.Failure("Invoice not found.", 404));

        var result = await InvoiceRelease.ReconcileAsync(db, payday, CompanyId, CancellationToken.None);

        result.InvoicesReleased.Should().Be(1);
        result.RecordsReleased.Should().Be(3);
        (await db.TimeEntries.SingleAsync()).IsInvoiced.Should().BeFalse();
    }

    [Fact]
    public async Task A_draft_cancelled_inside_Payday_is_released()
    {
        await using var db = NewDb();
        SeedDraft(db);
        var payday = new FakePayday(id =>
            ApiResult<PaydayInvoice>.Success(new PaydayInvoice { Id = Guid.Parse(id), Status = "CANCELLED" }));

        (await InvoiceRelease.ReconcileAsync(db, payday, CompanyId, CancellationToken.None))
            .InvoicesReleased.Should().Be(1);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(401)]
    [InlineData(null)]   // a timeout: no answer at all
    public async Task Payday_being_unreachable_releases_nothing(int? statusCode)
    {
        // Releasing on a guess could free hours that sit on a live draft and
        // have them billed twice. Not knowing means leaving it alone.
        await using var db = NewDb();
        SeedDraft(db);
        var payday = new FakePayday(_ => ApiResult<PaydayInvoice>.Failure("Payday is not answering.", statusCode));

        var result = await InvoiceRelease.ReconcileAsync(db, payday, CompanyId, CancellationToken.None);

        result.InvoicesReleased.Should().Be(0);
        result.Unreachable.Should().Be(1);
        (await db.TimeEntries.SingleAsync()).IsInvoiced.Should().BeTrue();
        (await db.ExpenseLineBillings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_draft_still_in_Payday_keeps_its_records_and_picks_up_its_number_once_issued()
    {
        await using var db = NewDb();
        SeedDraft(db);
        var payday = new FakePayday(id =>
            ApiResult<PaydayInvoice>.Success(new PaydayInvoice { Id = Guid.Parse(id), Status = "SENT", Number = 4213 }));

        var result = await InvoiceRelease.ReconcileAsync(db, payday, CompanyId, CancellationToken.None);

        result.InvoicesReleased.Should().Be(0);
        result.NumbersFilled.Should().Be(3);
        var entry = await db.TimeEntries.SingleAsync();
        entry.IsInvoiced.Should().BeTrue();
        entry.PaydayInvoiceNumber.Should().Be(4213);
    }
}
