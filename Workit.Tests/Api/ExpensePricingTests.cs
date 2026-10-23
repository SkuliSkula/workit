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
/// ExpensePricing is the only thing that sets a purchase line's sale price — its
/// Ein.verð, read the way the expense's total says — and it leaves alone what an
/// owner priced by hand and what has already been billed.
/// </summary>
public class ExpensePricingTests
{
    private readonly Guid companyId = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"pricing-{Guid.NewGuid()}").Options);

    /// <summary>The Húsasmiðjan LED strip from the report, priced the old way: cost × 1.5 = 58,076.</summary>
    private ExpenseLine Purchase(WorkitDbContext db, decimal unit = 70395.1613m, decimal discount = 45m, decimal quantity = 1m,
        decimal? total = null, decimal sale = 58076m)
    {
        var expense = new Expense
        {
            CompanyId = companyId, PaydayId = Guid.NewGuid(), Source = DataSource.Payday, CreditorName = "Húsasmiðjan ehf.", Status = "PAID",
            AmountExcludingVat = total ?? Math.Round(quantity * unit * (1 - discount / 100), 0),
        };
        var line = new ExpenseLine
        {
            ExpenseId = expense.Id, CompanyId = companyId, PaydayId = Guid.NewGuid(),
            Description = "6268002 - PROFILED BORÐI 230V 25m 3000K 1400lm/m 14W/m IP67 032190-25m", Quantity = quantity,
            UnitPriceExcludingVat = unit, DiscountPercentage = discount, VatPercentage = 24, MarkupFactor = 1.5m, SalePriceExcludingVat = sale,
        };
        expense.Lines.Add(line);
        db.Expenses.Add(expense);
        db.SaveChanges();
        return line;
    }

    [Fact]
    public async Task The_reported_LED_strip_is_repriced_to_its_Ein_verd()
    {
        await using var db = NewDb();
        var line = Purchase(db);

        (await ExpensePricing.RepriceAsync(db, companyId, CancellationToken.None)).Should().Be(1);

        var after = await db.ExpenseLines.SingleAsync(l => l.Id == line.Id);
        after.SalePriceExcludingVat.Should().Be(70395m, "not 58,076");
        after.MarkupFactor.Should().Be(1m, "no markup");
    }

    [Fact]
    public async Task An_expense_whose_Ein_verd_holds_the_VAT_is_priced_without_it()
    {
        await using var db = NewDb();
        // 87,290 stored as if without VAT; the expense total says it is with VAT.
        var line = Purchase(db, unit: 87290m, total: 38717m, sale: 0);

        await ExpensePricing.RepriceAsync(db, companyId, CancellationToken.None);

        (await db.ExpenseLines.SingleAsync(l => l.Id == line.Id)).SalePriceExcludingVat.Should().Be(70395m);
    }

    [Fact]
    public async Task A_price_an_owner_set_is_left_alone()
    {
        await using var db = NewDb();
        var line = Purchase(db, sale: 65000m);
        line.SalePriceSetByOwner = true;
        await db.SaveChangesAsync();

        (await ExpensePricing.RepriceAsync(db, companyId, CancellationToken.None)).Should().Be(0);
        (await db.ExpenseLines.SingleAsync(l => l.Id == line.Id)).SalePriceExcludingVat.Should().Be(65000m);
    }

    [Fact]
    public async Task A_line_already_billed_keeps_the_price_it_was_billed_at()
    {
        await using var db = NewDb();
        var line = Purchase(db);
        db.ExpenseLineBillings.Add(new ExpenseLineBilling { CompanyId = companyId, ExpenseLineId = line.Id, Quantity = 1, SalePriceExcludingVat = 58076m, VatPercentage = 24 });
        await db.SaveChangesAsync();

        (await ExpensePricing.RepriceAsync(db, companyId, CancellationToken.None)).Should().Be(0);
        (await db.ExpenseLines.SingleAsync(l => l.Id == line.Id)).SalePriceExcludingVat.Should().Be(58076m);
    }

    [Fact]
    public async Task Another_companys_purchases_are_never_touched()
    {
        await using var db = NewDb();
        var mine = Purchase(db);
        var other = new Expense { CompanyId = Guid.NewGuid(), AmountExcludingVat = 550m };
        other.Lines.Add(new ExpenseLine { CompanyId = other.CompanyId, ExpenseId = other.Id, Quantity = 1, UnitPriceExcludingVat = 1000m, DiscountPercentage = 45m, SalePriceExcludingVat = 825m });
        db.Expenses.Add(other);
        await db.SaveChangesAsync();

        await ExpensePricing.RepriceAsync(db, companyId, CancellationToken.None);

        (await db.ExpenseLines.SingleAsync(l => l.CompanyId == other.CompanyId)).SalePriceExcludingVat.Should().Be(825m);
        (await db.ExpenseLines.SingleAsync(l => l.Id == mine.Id)).SalePriceExcludingVat.Should().Be(70395m);
    }

    [Fact]
    public async Task A_purchase_the_expense_sync_brings_in_bills_at_its_Ein_verd()
    {
        await using var db = NewDb();
        var payday = new OneExpense(new PaydayExpense
        {
            Id = Guid.NewGuid(), Status = "PAID", Creditor = new PaydayExpenseCreditor { Name = "Húsasmiðjan ehf." }, Date = new DateTime(2026, 10, 2),
            AmountExcludingVat = 38717m, AmountIncludingVat = 48009m,
            Lines =
            [
                new PaydayExpenseLine
                {
                    Id = Guid.NewGuid(), Description = "6268002 - PROFILED BORÐI 230V 25m 3000K 1400lm/m 14W/m IP67 032190-25m",
                    Quantity = 1, UnitPriceExcludingVat = 70395.1613m, UnitPriceIncludingVat = 87290m, VatPercentage = 24, DiscountPercentage = 45m,
                },
            ],
        });

        await new PaydayExpenseSyncService(db, payday, NullLogger<PaydayExpenseSyncService>.Instance).SyncAsync(companyId, CancellationToken.None);

        (await db.ExpenseLines.SingleAsync()).SalePriceExcludingVat.Should().Be(70395m);
    }

    private sealed class OneExpense(PaydayExpense expense) : IPaydayExpensesApi
    {
        public Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(int page = 1, int perPage = 25, string? include = "lines", string? dateFrom = null, string? dateTo = null, string? status = null, string order = "desc", string orderBy = "date") =>
            Task.FromResult(ApiResult<PaydayExpensesResponse>.Success(new PaydayExpensesResponse { Expenses = [expense], Page = 1, Pages = 1, PerPage = perPage, Total = 1 }));
        public Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines") => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string expenseId) => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync() => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync() => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId) => throw new NotSupportedException();
    }
}
