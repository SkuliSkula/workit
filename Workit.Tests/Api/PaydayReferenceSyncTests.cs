using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Api;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Payday's small reference data — company profile, users and the lookup
/// lists — is stored as snapshots, refreshed in place, and answers when Payday
/// can't be reached.
/// </summary>
public class PaydayReferenceSyncTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private readonly FakePayday payday = new();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"reference-sync-{Guid.NewGuid()}").Options);

    private PaydayReferenceSyncService Service(WorkitDbContext db) =>
        new(db, payday, payday, payday, payday, payday, NullLogger<PaydayReferenceSyncService>.Instance);

    [Fact]
    public async Task Every_list_is_stored_once_per_kind_and_refreshed_in_place()
    {
        await using var db = NewDb();

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Should().Be((8, 0));
        payday.Accounts[0].Name = "Vörukaup (renamed)";
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        (await db.PaydaySnapshots.CountAsync()).Should().Be(8, "company, users, expense accounts, payment types, ledger accounts and three pension lists");
        var accounts = await PaydaySnapshotStore.GetAsync<List<PaydayExpenseAccount>>(db, companyId, PaydaySnapshotStore.ExpenseAccounts, CancellationToken.None);
        accounts.Should().ContainSingle().Which.Name.Should().Be("Vörukaup (renamed)");
        var unions = await PaydaySnapshotStore.GetAsync<List<PaydayPensionFund>>(db, companyId, PaydaySnapshotStore.PensionFunds(PaydayPensionType.Stettarfelag), CancellationToken.None);
        unions.Should().ContainSingle().Which.Name.Should().Be("Rafiðnaðarsamband Íslands");
    }

    [Fact]
    public async Task When_Payday_is_down_the_last_copy_is_kept_and_answers()
    {
        await using var db = NewDb();
        await Service(db).SyncAsync(companyId, CancellationToken.None);
        payday.Fail = true;

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Should().Be((0, 8));

        var live = await payday.GetSalesLedgerAccountsAsync();
        var answer = await PaydaySnapshotStore.LiveOrStoredAsync(live, db, companyId, PaydaySnapshotStore.LedgerAccounts, CancellationToken.None);
        answer.IsSuccess.Should().BeTrue();
        answer.Value.Should().ContainSingle().Which.Code.Should().Be("3100");
        (await PaydaySnapshotStore.GetAsync<PaydayCompany>(db, companyId, PaydaySnapshotStore.Company, CancellationToken.None))!.VatNumber.Should().Be("123456");
    }

    [Fact]
    public async Task With_nothing_stored_Paydays_failure_comes_through()
    {
        await using var db = NewDb();
        payday.Fail = true;

        var answer = await PaydaySnapshotStore.LiveOrStoredAsync(await payday.GetAccountsAsync(), db, companyId, PaydaySnapshotStore.ExpenseAccounts, CancellationToken.None);

        answer.IsSuccess.Should().BeFalse();
        answer.ErrorMessage.Should().Be("Payday is down.");
    }

    [Fact]
    public async Task Another_companys_snapshot_never_answers()
    {
        await using var db = NewDb();
        await Service(db).SyncAsync(Guid.NewGuid(), CancellationToken.None);
        payday.Fail = true;

        var answer = await PaydaySnapshotStore.LiveOrStoredAsync(await payday.GetAccountsAsync(), db, companyId, PaydaySnapshotStore.ExpenseAccounts, CancellationToken.None);

        answer.IsSuccess.Should().BeFalse();
    }

    private sealed class FakePayday : IPaydayCompaniesApi, IPaydayUsersApi, IPaydayExpensesApi, IPaydayProductsApi, IPaydayPensionApi
    {
        public bool Fail { get; set; }
        public List<PaydayExpenseAccount> Accounts { get; } = [new() { Id = Guid.NewGuid(), Name = "Vörukaup" }];

        private Task<ApiResult<T>> Answer<T>(T value) =>
            Task.FromResult(Fail ? ApiResult<T>.Failure("Payday is down.") : ApiResult<T>.Success(value));

        public Task<ApiResult<PaydayCompany>> GetMeAsync() => Answer(new PaydayCompany { Id = Guid.NewGuid(), Name = "ÓS rafverktakar ehf.", VatNumber = "123456" });
        Task<ApiResult<PaydayUser>> IPaydayUsersApi.GetMeAsync() => throw new NotSupportedException();
        Task<ApiResult<List<PaydayUser>>> IPaydayUsersApi.GetAllAsync() => Answer(new List<PaydayUser> { new() { Id = Guid.NewGuid(), Name = "Skúli", IsOwner = true } });
        Task<ApiResult<PaydayUser>> IPaydayUsersApi.GetByIdAsync(string userId) => throw new NotSupportedException();
        public Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync() => Answer(Accounts.Select(a => new PaydayExpenseAccount { Id = a.Id, Name = a.Name }).ToList());
        public Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync() => Answer(new List<PaydayPaymentTypeRef> { new() { Id = Guid.NewGuid(), Name = "Millifærsla" } });
        public Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync() => Answer(new List<PaydayLedgerAccount> { new() { Id = Guid.NewGuid(), Code = "3100", Name = "Sala" } });
        public Task<ApiResult<List<PaydayPensionFund>>> GetByTypeAsync(PaydayPensionType type) =>
            Answer(new List<PaydayPensionFund> { new() { Id = Guid.NewGuid(), Number = (int)type, Name = type == PaydayPensionType.Stettarfelag ? "Rafiðnaðarsamband Íslands" : $"Sjóður {type}" } });

        // Not read by the reference sync.
        public Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(int page = 1, int perPage = 25, string? include = "lines", string? dateFrom = null, string? dateTo = null, string? status = null, string order = "desc", string orderBy = "date") => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines") => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string expenseId) => throw new NotSupportedException();
        public Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId) => throw new NotSupportedException();
        Task<ApiResult<PaydayProductsResponse>> IPaydayProductsApi.GetAllAsync(int page, int perPage, string orderBy, string order, string? query) => throw new NotSupportedException();
        Task<ApiResult<PaydayProduct>> IPaydayProductsApi.GetByIdAsync(Guid id) => throw new NotSupportedException();
        Task<ApiResult<PaydayProduct>> IPaydayProductsApi.GetBySkuAsync(string sku) => throw new NotSupportedException();
        Task<ApiResult<PaydayProductMovementsResponse>> IPaydayProductsApi.GetMovementsAsync(Guid id, int page, int perPage) => throw new NotSupportedException();
        Task<ApiResult<PaydayProduct>> IPaydayProductsApi.CreateAsync(CreateProductRequest request) => throw new NotSupportedException();
    }
}
