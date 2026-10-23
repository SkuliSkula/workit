using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Api.Payday;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Workit keeps its own copy of every Payday expense. Payday's half — the
/// purchase — follows Payday; Workit's half — job link, markup, an owner's sale
/// price, billings — must survive every sync untouched.
/// </summary>
public class PaydayExpenseSyncTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private readonly FakeExpenses payday = new();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"expense-sync-{Guid.NewGuid()}").Options);

    private PaydayExpenseSyncService Service(WorkitDbContext db) => new(db, payday, NullLogger<PaydayExpenseSyncService>.Instance);

    /// <summary>A Rönning purchase: 10 m of cable at 981 list, 27 % vendor discount.</summary>
    private static PaydayExpense Remote(string status = "UNPAID", string? comments = null, decimal listPrice = 981m, decimal discount = 27m) => new()
    {
        Id                 = Guid.NewGuid(),
        Status             = status,
        Creditor           = new PaydayExpenseCreditor { Id = Guid.NewGuid(), Name = "Rönning", Ssn = "6908942509" },
        Date               = new DateTime(2026, 10, 3),
        DueDate            = new DateTime(2026, 10, 20),
        Comments           = comments,
        AmountExcludingVat = 7_161m,
        AmountIncludingVat = 8_880m,
        AmountVat          = 1_719m,
        Created            = new DateTime(2026, 10, 3, 10, 0, 0),
        Lines =
        [
            new PaydayExpenseLine
            {
                Id = Guid.NewGuid(), Description = "Ídráttarvír 1,5q", Quantity = 10m,
                UnitPriceExcludingVat = listPrice, UnitPriceIncludingVat = listPrice * 1.24m, VatPercentage = 24m, DiscountPercentage = discount,
            },
        ],
    };

    [Fact]
    public async Task A_sync_stores_every_expense_priced_at_its_Ein_verd()
    {
        await using var db = NewDb();
        for (var i = 0; i < 130; i++) payday.Expenses.Add(Remote());

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Added.Should().Be(130);
        (await db.Expenses.CountAsync(e => e.CompanyId == companyId)).Should().Be(130);
        var line = await db.ExpenseLines.FirstAsync();
        line.SalePriceExcludingVat.Should().Be(981m, "the Ein.verð — no markup on the 716.13 paid");
        line.MarkupFactor.Should().Be(1m);
        var expense = await db.Expenses.FirstAsync();
        expense.JobId.Should().BeNull("linking to jobs is the auto-link's job, within the owner's window");
        expense.Date!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task A_second_sync_follows_Payday_but_keeps_the_job_link_markup_and_typed_price()
    {
        await using var db = NewDb();
        var remote = Remote();
        payday.Expenses.Add(remote);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        // The owner links it to a job and types their own price.
        var jobId = Guid.NewGuid();
        var stored = await db.Expenses.Include(e => e.Lines).SingleAsync();
        stored.JobId = jobId;
        stored.JobLinkSource = ExpenseJobLinkSource.Manual;
        stored.Lines[0].MarkupFactor = 1.3m;
        stored.Lines[0].SalePriceExcludingVat = 999m;
        stored.Lines[0].SalePriceSetByOwner = true;   // what PUT /expenses/{id}/lines/{lineId} records
        await db.SaveChangesAsync();

        // Payday: paid now, and the vendor corrected the discount.
        remote.Status = "PAID";
        remote.PaidDate = new DateTime(2026, 10, 18);
        remote.Lines![0].DiscountPercentage = 30m;

        await Service(db).SyncAsync(companyId, CancellationToken.None);

        var after = await db.Expenses.Include(e => e.Lines).SingleAsync();
        after.Status.Should().Be("PAID");
        after.JobId.Should().Be(jobId);
        after.JobLinkSource.Should().Be(ExpenseJobLinkSource.Manual);
        after.Lines[0].DiscountPercentage.Should().Be(30m);
        after.Lines[0].MarkupFactor.Should().Be(1.3m);
        after.Lines[0].SalePriceExcludingVat.Should().Be(999m, "an owner's price is theirs");
    }

    [Fact]
    public async Task A_price_Workit_worked_out_follows_a_vendor_correction()
    {
        await using var db = NewDb();
        var remote = Remote();
        payday.Expenses.Add(remote);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        // The vendor re-issues with a new Ein.verð (and the total that goes with it).
        remote.Lines![0].UnitPriceExcludingVat = 1000m;
        remote.AmountExcludingVat = 7_300m;
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        (await db.ExpenseLines.SingleAsync()).SalePriceExcludingVat.Should().Be(1000m);
    }

    [Fact]
    public async Task A_billed_line_is_never_removed_even_when_Payday_drops_it()
    {
        await using var db = NewDb();
        var remote = Remote();
        payday.Expenses.Add(remote);
        await Service(db).SyncAsync(companyId, CancellationToken.None);
        var line = await db.ExpenseLines.SingleAsync();
        db.ExpenseLineBillings.Add(new ExpenseLineBilling { CompanyId = companyId, ExpenseLineId = line.Id, Quantity = 4, SalePriceExcludingVat = 1_074m });
        await db.SaveChangesAsync();

        remote.Lines!.Clear();
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        (await db.ExpenseLines.CountAsync()).Should().Be(1);
        (await db.ExpenseLineBillings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_expense_deleted_in_Payday_leaves_Workit_unless_something_was_billed_from_it()
    {
        await using var db = NewDb();
        var unbilled = SeedStored(db, billed: false);
        var billed   = SeedStored(db, billed: true);

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Removed.Should().Be(2);
        (await db.Expenses.AnyAsync(e => e.PaydayId == unbilled)).Should().BeFalse();
        var kept = await db.Expenses.SingleAsync(e => e.PaydayId == billed);
        kept.Status.Should().Be(PaydayExpenseSyncService.DeletedStatus);
        (await db.ExpenseLineBillings.CountAsync()).Should().Be(1, "the invoice it went on still says so");

        var listed = await PaydayExpenseStore.ListAsync(db, companyId, new PaydayExpenseStore.ListQuery(), CancellationToken.None);
        listed.Total.Should().Be(0, "Payday has no deleted expenses to list");
        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Result!.Removed.Should().Be(0, "it is not dropped twice");
    }

    [Fact]
    public async Task Nothing_is_removed_when_Payday_lists_fewer_expenses_than_it_says_it_has()
    {
        await using var db = NewDb();
        var stored = SeedStored(db, billed: false);
        payday.Expenses.Add(Remote());
        payday.ReportedTotal = 2;

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Result!.Removed.Should().Be(0);
        (await db.Expenses.AnyAsync(e => e.PaydayId == stored)).Should().BeTrue();
    }

    [Fact]
    public async Task A_lagging_list_does_not_undo_an_edit_Workit_just_stored()
    {
        await using var db = NewDb();
        var remote = Remote(status: "UNPAID");
        payday.Expenses.Add(remote);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        var paid = Remote(status: "PAID");
        paid.Id = remote.Id;
        paid.Lines = null;   // an edit response without lines
        await Service(db).SaveAsync(companyId, paid, CancellationToken.None);
        await Service(db).SyncAsync(companyId, CancellationToken.None);   // Payday still lists UNPAID

        var after = await db.Expenses.Include(e => e.Lines).SingleAsync();
        after.Status.Should().Be("PAID");
        after.Lines.Should().ContainSingle("a header-only response leaves the lines alone");
    }

    [Fact]
    public async Task When_Payday_cannot_be_reached_the_stored_copy_is_left_as_it_is()
    {
        await using var db = NewDb();
        SeedStored(db, billed: false);
        payday.Fail = true;

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Error.Should().NotBeNull();
        (await db.Expenses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Job_codes_are_found_in_the_stored_copy_and_an_owners_unlink_still_wins()
    {
        await using var db = NewDb();
        var job = new Job { CompanyId = companyId, Code = "MNT102", JobNumber = 102, Category = JobCategory.Maintenance, Name = "Sumarhús" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var coded    = Remote(comments: "Verk MNT102");
        var unlinked = Remote(comments: "MNT102 líka");
        payday.Expenses.AddRange([coded, unlinked]);
        await Service(db).SyncAsync(companyId, CancellationToken.None);
        var removed = await db.Expenses.SingleAsync(e => e.PaydayId == unlinked.Id);
        removed.JobLinkSource = ExpenseJobLinkSource.RemovedByOwner;
        await db.SaveChangesAsync();

        var stored = await PaydayExpenseStore.AllAsync(db, companyId, "2026-10-01", "2026-10-31", CancellationToken.None);
        var outcome = await ExpenseAutoLinkEndpoints.LinkAsync(db, companyId, stored, [job], CancellationToken.None);

        outcome.Linked.Should().ContainSingle().Which.PaydayExpenseId.Should().Be(coded.Id);
        (await db.Expenses.SingleAsync(e => e.PaydayId == coded.Id)).JobId.Should().Be(job.Id);
        (await db.Expenses.SingleAsync(e => e.PaydayId == unlinked.Id)).JobId.Should().BeNull();
        (await db.Expenses.CountAsync()).Should().Be(2, "linking never adds a second copy");
    }

    [Fact]
    public async Task The_stored_list_answers_with_Paydays_filters_and_order()
    {
        await using var db = NewDb();
        var october  = Remote(status: "PAID");
        var november = Remote(status: "UNPAID");
        november.Date = new DateTime(2026, 11, 2);
        payday.Expenses.AddRange([october, november]);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        var all = await PaydayExpenseStore.ListAsync(db, companyId, new PaydayExpenseStore.ListQuery(), CancellationToken.None);
        all.Expenses.Select(e => e.Id).Should().Equal(november.Id, october.Id);
        all.Expenses[0].Lines.Should().ContainSingle().Which.DiscountPercentage.Should().Be(27m);
        all.Expenses[0].Creditor!.Name.Should().Be("Rönning");

        var inOctober = await PaydayExpenseStore.ListAsync(db, companyId,
            new PaydayExpenseStore.ListQuery(DateFrom: "2026-10-01", DateTo: "2026-10-31", Include: null), CancellationToken.None);
        inOctober.Expenses.Should().ContainSingle().Which.Lines.Should().BeNull();

        var unpaid = await PaydayExpenseStore.ListAsync(db, companyId, new PaydayExpenseStore.ListQuery(Status: "UNPAID"), CancellationToken.None);
        unpaid.Expenses.Should().ContainSingle().Which.Id.Should().Be(november.Id);
    }

    private Guid SeedStored(WorkitDbContext db, bool billed)
    {
        var paydayId = Guid.NewGuid();
        var expense = new Expense
        {
            CompanyId = companyId, PaydayId = paydayId, Source = DataSource.Payday, Status = "UNPAID",
            SyncedAt = DateTimeOffset.UtcNow.AddHours(-1), CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        var line = new ExpenseLine { ExpenseId = expense.Id, CompanyId = companyId, PaydayId = Guid.NewGuid(), Description = "Tafla", Quantity = 1 };
        expense.Lines.Add(line);
        db.Expenses.Add(expense);
        if (billed)
            db.ExpenseLineBillings.Add(new ExpenseLineBilling { CompanyId = companyId, ExpenseLineId = line.Id, Quantity = 1, SalePriceExcludingVat = 5_000m });
        db.SaveChanges();
        return paydayId;
    }

    private sealed class FakeExpenses : IPaydayExpensesApi
    {
        public List<PaydayExpense> Expenses { get; } = [];
        public int? ReportedTotal { get; set; }
        public bool Fail { get; set; }

        public Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(int page = 1, int perPage = 25, string? include = "lines",
            string? dateFrom = null, string? dateTo = null, string? status = null, string order = "desc", string orderBy = "date")
        {
            if (Fail) return Task.FromResult(ApiResult<PaydayExpensesResponse>.Failure("Payday is down."));
            return Task.FromResult(ApiResult<PaydayExpensesResponse>.Success(new PaydayExpensesResponse
            {
                Expenses = Expenses.Skip((page - 1) * perPage).Take(perPage).ToList(),
                Page     = page,
                PerPage  = perPage,
                Total    = ReportedTotal ?? Expenses.Count,
                Pages    = Math.Max(1, (int)Math.Ceiling(Expenses.Count / (double)perPage)),
            }));
        }

        public Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines") => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string expenseId) => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync() => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync() => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId) => throw new NotSupportedException();
    }
}
