using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// The persisting half of automatic linking: which expenses get linked,
/// which are left alone, and what is reported back.
/// </summary>
public class ExpenseAutoLinkTests
{
    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"autolink-{Guid.NewGuid()}")
            .Options);

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Job Rep107 = new() { CompanyId = CompanyId, Name = "Raflagnir", Code = "REP107", JobNumber = 107, Category = JobCategory.Repair };
    private static readonly Job Mnt004 = new() { CompanyId = CompanyId, Name = "Viðhald", Code = "MNT004", JobNumber = 4, Category = JobCategory.Maintenance };

    private static PaydayExpense Payday(string? comments, string? reference = null, decimal amount = 1000m) => new()
    {
        Id = Guid.NewGuid(), Comments = comments, Reference = reference, AmountIncludingVat = amount,
        Creditor = new PaydayExpenseCreditor { Name = "Rönning" },
        Lines = [new PaydayExpenseLine { Description = "Efni", Quantity = 1, UnitPriceExcludingVat = 800m }],
    };

    [Fact]
    public async Task Links_AnUnimportedExpense_ThatNamesOneJob()
    {
        await using var db = NewDb();
        var pe = Payday("Efni fyrir REP107");

        var result = await ExpenseAutoLinkEndpoints.LinkAsync(db, CompanyId, [pe], [Rep107, Mnt004], CancellationToken.None);

        result.Scanned.Should().Be(1);
        result.Linked.Should().ContainSingle().Which.JobCode.Should().Be("REP107");
        var saved = await db.Expenses.Include(e => e.Lines).SingleAsync();
        saved.PaydayId.Should().Be(pe.Id);
        saved.JobId.Should().Be(Rep107.Id);
        saved.JobLinkSource.Should().Be(ExpenseJobLinkSource.Automatic);
        saved.CompanyId.Should().Be(CompanyId);
        saved.Lines.Should().ContainSingle().Which.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task Links_AnAlreadyImportedButUnlinkedExpense_WithoutDuplicatingIt()
    {
        await using var db = NewDb();
        var pe = Payday("verk 4");
        db.Expenses.Add(new Expense { CompanyId = CompanyId, PaydayId = pe.Id, JobLinkSource = ExpenseJobLinkSource.None });
        await db.SaveChangesAsync();

        var result = await ExpenseAutoLinkEndpoints.LinkAsync(db, CompanyId, [pe], [Rep107, Mnt004], CancellationToken.None);

        result.Linked.Should().ContainSingle().Which.JobCode.Should().Be("MNT004");
        var all = await db.Expenses.ToListAsync();
        all.Should().ContainSingle();
        all[0].JobId.Should().Be(Mnt004.Id);
        all[0].JobLinkSource.Should().Be(ExpenseJobLinkSource.Automatic);
    }

    [Fact]
    public async Task LeavesAlone_ManualLinks_AndOwnerRemovedLinks()
    {
        await using var db = NewDb();
        var manual   = Payday("REP107");
        var removed  = Payday("REP107");
        db.Expenses.AddRange(
            new Expense { CompanyId = CompanyId, PaydayId = manual.Id,  JobId = Mnt004.Id, JobLinkSource = ExpenseJobLinkSource.Manual },
            new Expense { CompanyId = CompanyId, PaydayId = removed.Id, JobId = null,      JobLinkSource = ExpenseJobLinkSource.RemovedByOwner });
        await db.SaveChangesAsync();

        var result = await ExpenseAutoLinkEndpoints.LinkAsync(db, CompanyId, [manual, removed], [Rep107, Mnt004], CancellationToken.None);

        result.Linked.Should().BeEmpty();
        result.Skipped.Should().Be(2);
        (await db.Expenses.SingleAsync(e => e.PaydayId == manual.Id)).JobId.Should().Be(Mnt004.Id);
        (await db.Expenses.SingleAsync(e => e.PaydayId == removed.Id)).JobId.Should().BeNull();
    }

    [Fact]
    public async Task Reports_AmbiguousExpenses_InsteadOfGuessing()
    {
        await using var db = NewDb();
        var pe = Payday("REP107 og MNT004");

        var result = await ExpenseAutoLinkEndpoints.LinkAsync(db, CompanyId, [pe], [Rep107, Mnt004], CancellationToken.None);

        result.Linked.Should().BeEmpty();
        result.Ambiguous.Should().ContainSingle().Which.JobCodes.Should().Equal("REP107", "MNT004");
        (await db.Expenses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DoesNothing_ForExpensesWithoutAJobReference()
    {
        await using var db = NewDb();

        var result = await ExpenseAutoLinkEndpoints.LinkAsync(db, CompanyId, [Payday("Kaffi og kleinur")], [Rep107], CancellationToken.None);

        result.Scanned.Should().Be(1);
        result.Linked.Should().BeEmpty();
        result.Ambiguous.Should().BeEmpty();
        (await db.Expenses.CountAsync()).Should().Be(0);
    }
}
