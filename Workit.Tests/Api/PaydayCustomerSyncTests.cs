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
/// Customers two-way: a push links the row and clears the pending flag, a
/// refused push leaves Payday's message on the row, and the pull upserts by
/// Payday id then SSN with Payday winning billing fields while Workit-only
/// fields survive. Payday is faked in memory.
/// </summary>
public class PaydayCustomerSyncTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class FakeCustomers : IPaydayCustomersApi
    {
        public List<PaydayCustomer> Remote { get; } = [];
        public string? FailWith { get; set; }
        public List<CreateCustomerRequest> Created { get; } = [];
        public List<(string Id, UpdateCustomerRequest Body)> Updated { get; } = [];

        public Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100) =>
            Task.FromResult(FailWith is not null
                ? ApiResult<PaydayCustomersResponse>.Failure(FailWith)
                : ApiResult<PaydayCustomersResponse>.Success(new PaydayCustomersResponse { Customers = Remote.ToList(), Page = 1, Pages = 1, PerPage = perPage, Total = Remote.Count }));

        public Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request)
        {
            Created.Add(request);
            if (FailWith is not null) return Task.FromResult(ApiResult<PaydayCustomer>.Failure(FailWith));
            // Payday resolves an Icelandic SSN in Registers Iceland and returns the registered name.
            var created = new PaydayCustomer { Id = Guid.NewGuid(), Ssn = request.Ssn, Name = request.Ssn is null ? request.Name : "Registered " + request.Name, Address = request.Address, Email = request.Email, Phone = request.Phone };
            Remote.Add(created);
            return Task.FromResult(ApiResult<PaydayCustomer>.Success(created));
        }

        public Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request)
        {
            Updated.Add((customerId, request));
            if (FailWith is not null) return Task.FromResult(ApiResult<PaydayCustomer>.Failure(FailWith));
            var remote = Remote.Single(r => r.Id.ToString() == customerId);
            remote.Address = request.Address ?? remote.Address;
            remote.Email = request.Email ?? remote.Email;
            return Task.FromResult(ApiResult<PaydayCustomer>.Success(remote));
        }

        public Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId) => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25) => throw new NotSupportedException();
        public Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100) => throw new NotSupportedException();
    }

    private static PaydayCustomerSyncService Service(WorkitDbContext db, IPaydayCustomersApi payday) =>
        new(db, payday, NullLogger<PaydayCustomerSyncService>.Instance);

    private static WorkitDbContext NewDb()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"pdcust-{Guid.NewGuid()}").Options);
        db.Companies.Add(new Company { Id = CompanyId, Name = "Test" });
        db.SaveChanges();
        return db;
    }

    private static Customer Local(string name, string ssn, Guid? paydayId = null, bool planInTasks = false) => new()
    {
        CompanyId = CompanyId, Name = name, Ssn = ssn, Address = "Local street 1", Email = "local@example.is",
        PaydayId = paydayId, PlanJobsInTasks = planInTasks,
    };

    [Fact]
    public async Task Push_NewCustomer_CreatesInPayday_LinksRow_AndTakesRegisteredName()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        var customer = Local("Jon Jonsson", "010180-1239");
        db.Customers.Add(customer);

        var ok = await Service(db, payday).PushAsync(customer, CancellationToken.None);

        ok.Should().BeTrue();
        payday.Created.Should().ContainSingle().Which.Ssn.Should().Be("0101801239", "Payday gets digits only");
        customer.PaydayId.Should().Be(payday.Remote.Single().Id);
        customer.PaydayPushPending.Should().BeFalse();
        customer.PaydayPushError.Should().BeNull();
        customer.PaydaySyncedAt.Should().NotBeNull();
        customer.Name.Should().Be("Registered Jon Jonsson", "Payday's registry name wins");
    }

    [Fact]
    public async Task Push_LinkedCustomer_UpdatesInPayday()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        var remoteId = Guid.NewGuid();
        payday.Remote.Add(new PaydayCustomer { Id = remoteId, Ssn = "0101801239", Name = "Jon" });
        var customer = Local("Jon", "0101801239", remoteId);

        var ok = await Service(db, payday).PushAsync(customer, CancellationToken.None);

        ok.Should().BeTrue();
        payday.Created.Should().BeEmpty();
        payday.Updated.Should().ContainSingle().Which.Id.Should().Be(remoteId.ToString());
        payday.Remote.Single().Address.Should().Be("Local street 1");
    }

    [Fact]
    public async Task Push_Refused_FlagsPending_WithPaydaysMessage_AndSyncRetries()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers { FailWith = "Invalid SSN." };
        var customer = Local("Broken", "1234567890");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        (await Service(db, payday).PushAsync(customer, CancellationToken.None)).Should().BeFalse();
        customer.PaydayPushPending.Should().BeTrue();
        customer.PaydayPushError.Should().Be("Invalid SSN.");
        customer.PaydayId.Should().BeNull();
        await db.SaveChangesAsync();

        // Payday is happy again: the next sync delivers the pending edit before pulling.
        payday.FailWith = null;
        var outcome = await Service(db, payday).SyncAsync(CompanyId, CancellationToken.None);

        outcome.Error.Should().BeNull();
        outcome.Result!.Pushed.Should().Be(1);
        outcome.Result.PushFailed.Should().Be(0);
        var saved = await db.Customers.SingleAsync();
        saved.PaydayPushPending.Should().BeFalse();
        saved.PaydayPushError.Should().BeNull();
        saved.PaydayId.Should().NotBeNull();
    }

    [Fact]
    public async Task Sync_LinksBySsn_PaydayWinsBilling_WorkitKeepsItsOwnFields()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        var remoteId = Guid.NewGuid();
        payday.Remote.Add(new PaydayCustomer { Id = remoteId, Ssn = "0101801239", Name = "Jón Jónsson ehf.", Address = "Payday street 9", City = "Reykjavík" });
        db.Customers.Add(Local("Jon Jonsson", "010180-1239", planInTasks: true));
        await db.SaveChangesAsync();

        var outcome = await Service(db, payday).SyncAsync(CompanyId, CancellationToken.None);

        outcome.Error.Should().BeNull();
        outcome.Result!.Linked.Should().Be(1);
        outcome.Result.Added.Should().Be(0);
        var saved = await db.Customers.SingleAsync();
        saved.PaydayId.Should().Be(remoteId);
        saved.Source.Should().Be(DataSource.Payday);
        saved.Name.Should().Be("Jón Jónsson ehf.");
        saved.Address.Should().Be("Payday street 9");
        saved.Ssn.Should().Be("0101801239");
        saved.PlanJobsInTasks.Should().BeTrue("Workit-only fields are never touched by the pull");
    }

    [Fact]
    public async Task Sync_AddsUnknownPaydayCustomers_AndUpdatesLinkedOnes()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        var linkedId = Guid.NewGuid();
        payday.Remote.Add(new PaydayCustomer { Id = linkedId, Ssn = "0101801239", Name = "Linked", Email = "new@payday.is" });
        payday.Remote.Add(new PaydayCustomer { Id = Guid.NewGuid(), Ssn = "0202802229", Name = "Brand new", Address = "Somewhere 2" });
        db.Customers.Add(Local("Linked", "0101801239", linkedId));
        await db.SaveChangesAsync();

        var outcome = await Service(db, payday).SyncAsync(CompanyId, CancellationToken.None);

        outcome.Result!.Fetched.Should().Be(2);
        outcome.Result.Updated.Should().Be(1);
        outcome.Result.Added.Should().Be(1);
        var all = await db.Customers.OrderBy(c => c.Name).ToListAsync();
        all.Should().HaveCount(2);
        all.Single(c => c.Name == "Linked").Email.Should().Be("new@payday.is");
        var added = all.Single(c => c.Name == "Brand new");
        added.Source.Should().Be(DataSource.Payday);
        added.CreatedByName.Should().Be("Payday sync");
        added.Ssn.Should().Be("0202802229");
    }

    [Fact]
    public async Task Push_SsnDroppedByPayday_LinksButWarns()
    {
        await using var db = NewDb();
        var payday = new DropsSsn();
        var customer = Local("Unknown to registry", "6703303339");

        (await Service(db, payday).PushAsync(customer, CancellationToken.None)).Should().BeTrue();

        customer.PaydayId.Should().NotBeNull();
        customer.PaydayPushPending.Should().BeFalse();
        customer.PaydayPushError.Should().Contain("did not recognise the SSN 6703303339");
        customer.Ssn.Should().Be("6703303339", "Workit keeps what the owner typed");
    }

    /// <summary>Payday that accepts the create but, not finding the SSN in the registry, stores none.</summary>
    private sealed class DropsSsn : IPaydayCustomersApi
    {
        public Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request) =>
            Task.FromResult(ApiResult<PaydayCustomer>.Success(new PaydayCustomer { Id = Guid.NewGuid(), Ssn = null, Name = request.Name }));
        public Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100) => throw new NotSupportedException();
        public Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId) => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25) => throw new NotSupportedException();
        public Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Sync_ForeignPaydayCustomer_GetsThePlaceholderSsn_SoItStaysEditable()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        payday.Remote.Add(new PaydayCustomer { Id = Guid.NewGuid(), Ssn = null, Name = "Foreign Ltd" });

        await Service(db, payday).SyncAsync(CompanyId, CancellationToken.None);

        (await db.Customers.SingleAsync()).Ssn.Should().Be("0000000000");
    }

    [Fact]
    public async Task Sync_PendingPushThatStillFails_KeepsWorkitValues()
    {
        await using var db = NewDb();
        var payday = new FakeCustomers();
        var remoteId = Guid.NewGuid();
        payday.Remote.Add(new PaydayCustomer { Id = remoteId, Ssn = "0101801239", Name = "Old name", Address = "Old street" });
        var local = Local("New name", "0101801239", remoteId);
        local.PaydayPushPending = true;
        db.Customers.Add(local);
        await db.SaveChangesAsync();
        payday.FailWith = "Payday is down";

        // Pull itself fails while Payday is down…
        var down = await Service(db, payday).SyncAsync(CompanyId, CancellationToken.None);
        down.Error.Should().Be("Payday is down");

        // …and once only the update keeps failing, the pull must not overwrite the newer Workit values.
        var flaky = new FlakyUpdate(payday);
        var outcome = await Service(db, flaky).SyncAsync(CompanyId, CancellationToken.None);
        outcome.Result!.PushFailed.Should().Be(1);
        (await db.Customers.SingleAsync()).Name.Should().Be("New name");
    }

    /// <summary>Payday that lists fine but refuses updates.</summary>
    private sealed class FlakyUpdate(FakeCustomers inner) : IPaydayCustomersApi
    {
        public Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100) { inner.FailWith = null; return inner.GetAllAsync(page, perPage); }
        public Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request) => Task.FromResult(ApiResult<PaydayCustomer>.Failure("Nope"));
        public Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request) => inner.CreateAsync(request);
        public Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId) => throw new NotSupportedException();
        public Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25) => throw new NotSupportedException();
        public Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Save_OneOpenedCustomer_UpdatesByPaydayId_LinksBySsn_OrAdds()
    {
        await using var db = NewDb();
        var linkedId = Guid.NewGuid();
        db.Customers.AddRange(
            Local("Gamla nafnið", "5609002260", paydayId: linkedId, planInTasks: true),
            Local("Kjarnafæði", "4709881589"));
        await db.SaveChangesAsync();
        var sync = Service(db, new FakeCustomers());

        await sync.SaveAsync(CompanyId, new PaydayCustomer { Id = linkedId, Name = "Seðlabankinn", Ssn = "5609002260", Email = "s@sedlabanki.is" }, CancellationToken.None);
        var bySsnId = Guid.NewGuid();
        await sync.SaveAsync(CompanyId, new PaydayCustomer { Id = bySsnId, Name = "Kjarnafæði hf.", Ssn = "470988-1589" }, CancellationToken.None);
        var newId = Guid.NewGuid();
        await sync.SaveAsync(CompanyId, new PaydayCustomer { Id = newId, Name = "Foreign GmbH" }, CancellationToken.None);

        var linked = await db.Customers.SingleAsync(c => c.PaydayId == linkedId);
        linked.Name.Should().Be("Seðlabankinn");
        linked.Email.Should().Be("s@sedlabanki.is");
        linked.PlanJobsInTasks.Should().BeTrue("Workit-only fields are Workit's");
        (await db.Customers.SingleAsync(c => c.PaydayId == bySsnId)).Name.Should().Be("Kjarnafæði hf.");
        (await db.Customers.SingleAsync(c => c.PaydayId == newId)).Ssn.Should().Be("0000000000");
        (await db.Customers.CountAsync()).Should().Be(3, "matching by SSN links the existing row instead of adding one");
    }

    [Fact]
    public async Task Save_KeepsAPendingWorkitEditOverPaydaysOlderValues()
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        var local = Local("Nýtt nafn", "5609002260", paydayId: id);
        local.PaydayPushPending = true;
        db.Customers.Add(local);
        await db.SaveChangesAsync();

        await Service(db, new FakeCustomers()).SaveAsync(CompanyId, new PaydayCustomer { Id = id, Name = "Gamalt nafn", Ssn = "5609002260" }, CancellationToken.None);

        (await db.Customers.SingleAsync()).Name.Should().Be("Nýtt nafn");
    }

    [Fact]
    public async Task Store_ListsTheLinkedCustomersInPaydaysShape()
    {
        await using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        db.Customers.AddRange(
            Local("Örn ehf.", "4709881589", paydayId: a),
            Local("Arnar", "0000000000", paydayId: b),
            Local("Not in Payday", "5609002260"),
            new Customer { CompanyId = Guid.NewGuid(), Name = "Other company", Ssn = "1111111119", PaydayId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var list = await PaydayCustomerStore.ListAsync(db, CompanyId, 1, 100, CancellationToken.None);

        list.Total.Should().Be(2);
        list.Customers.Select(c => c.Id).Should().Equal(b, a);
        list.Customers[0].Ssn.Should().BeNull("the placeholder means Payday has no SSN for it");
        list.Customers[1].Email.Should().Be("local@example.is");
        (await PaydayCustomerStore.GetAsync(db, CompanyId, a, CancellationToken.None))!.Name.Should().Be("Örn ehf.");
    }

    [Fact]
    public void NormalizeSsn_DigitsOnly_TenLong_NeverThePlaceholder()
    {
        PaydayCustomerSyncService.NormalizeSsn("010180-1239").Should().Be("0101801239");
        PaydayCustomerSyncService.NormalizeSsn("0000000000").Should().BeNull();
        PaydayCustomerSyncService.NormalizeSsn("12345").Should().BeNull();
        PaydayCustomerSyncService.NormalizeSsn("  ").Should().BeNull();
    }
}
