using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Payday's payroll employees, company profile and users are stored in Workit.
/// Workit employees are linked to Payday's by SSN — linked only, never rewritten.
/// </summary>
public class PaydayEmployeeSyncTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private readonly FakePayday payday = new();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"employee-sync-{Guid.NewGuid()}").Options);

    private PaydayEmployeeSyncService Service(WorkitDbContext db) =>
        new(db, payday);

    private static PaydayEmployee Remote(string name, string? ssn, bool active = true) => new()
    {
        Id = Guid.NewGuid(), Name = name, Ssn = ssn, Email = name.ToLower().Replace(' ', '.') + "@payday.is", Mobile = "6901234", Active = active,
    };

    private Employee Local(string name, string ssn) => new()
    {
        CompanyId = companyId, DisplayName = name, Ssn = ssn, Email = "local@workit.is", Phone = "7771234", HourlySalary = 4_500m,
    };

    [Fact]
    public async Task A_sync_stores_Paydays_employees_and_links_Workit_employees_by_SSN_without_rewriting_them()
    {
        await using var db = NewDb();
        var anna = Local("Anna Demósdóttir", "010180-1239");
        var nonni = Local("Nonni", "0000000000");
        db.Employees.AddRange(anna, nonni);
        await db.SaveChangesAsync();
        payday.Employees.AddRange([Remote("Anna Jónsdóttir", "0101801239"), Remote("Skrifstofa", "1203902719", active: false)]);

        var outcome = await Service(db).SyncAsync(companyId, CancellationToken.None);

        outcome.Result!.Added.Should().Be(2);
        outcome.Result.Linked.Should().Be(1);
        (await db.PaydayEmployees.CountAsync()).Should().Be(2);
        var linked = await db.Employees.SingleAsync(e => e.Id == anna.Id);
        linked.PaydayId.Should().Be(payday.Employees[0].Id);
        linked.DisplayName.Should().Be("Anna Demósdóttir", "the Workit employee is linked, not rewritten");
        linked.Email.Should().Be("local@workit.is");
        linked.HourlySalary.Should().Be(4_500m);
        (await db.Employees.SingleAsync(e => e.Id == nonni.Id)).PaydayId.Should().BeNull("the placeholder SSN matches no one");
        (await db.Employees.CountAsync()).Should().Be(2, "nobody is created — that stays the owner's Sync employees");
    }

    [Fact]
    public async Task An_employee_Payday_no_longer_lists_is_archived_and_comes_back_if_listed_again()
    {
        await using var db = NewDb();
        var leaver = Remote("Leaver", "1203902719");
        payday.Employees.Add(leaver);
        await Service(db).SyncAsync(companyId, CancellationToken.None);

        payday.Employees.Clear();
        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Result!.Archived.Should().Be(1);
        (await db.PaydayEmployees.SingleAsync()).Archived.Should().BeTrue();

        payday.Employees.Add(leaver);
        await Service(db).SyncAsync(companyId, CancellationToken.None);
        (await db.PaydayEmployees.SingleAsync()).Archived.Should().BeFalse();
    }

    [Fact]
    public async Task A_Payday_employee_already_linked_is_not_linked_to_a_second_Workit_employee()
    {
        await using var db = NewDb();
        var remote = Remote("Anna", "0101801239");
        var first = Local("Anna", "0101801239");
        first.PaydayId = remote.Id;
        db.Employees.AddRange(first, Local("Anna (duplicate)", "0101801239"));
        await db.SaveChangesAsync();
        payday.Employees.Add(remote);

        (await Service(db).SyncAsync(companyId, CancellationToken.None)).Result!.Linked.Should().Be(0);
        (await db.Employees.CountAsync(e => e.PaydayId == remote.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Opening_an_employee_stores_and_links_it()
    {
        await using var db = NewDb();
        var anna = Local("Anna", "0101801239");
        db.Employees.Add(anna);
        await db.SaveChangesAsync();
        var remote = Remote("Anna Jónsdóttir", "0101801239");

        await Service(db).SaveAsync(companyId, remote, CancellationToken.None);

        (await db.PaydayEmployees.SingleAsync()).Name.Should().Be("Anna Jónsdóttir");
        (await db.Employees.SingleAsync()).PaydayId.Should().Be(remote.Id);
    }

    private sealed class FakePayday : IPaydayEmployeesApi
    {
        public List<PaydayEmployee> Employees { get; } = [];

        public Task<ApiResult<List<PaydayEmployee>>> GetAllAsync() => Task.FromResult(ApiResult<List<PaydayEmployee>>.Success(Employees.ToList()));
        public Task<ApiResult<PaydayEmployee>> GetByIdAsync(string employeeId) => throw new NotSupportedException();
        public Task<ApiResult<PaydayEmployee>> CreateAsync(CreateEmployeeRequest request) => throw new NotSupportedException();
        public Task<ApiResult<PaydayEmployee>> UpdateAsync(string employeeId, UpdateEmployeeRequest request) => throw new NotSupportedException();
        public Task<ApiResult<bool>> DeleteAsync(string employeeId) => throw new NotSupportedException();
    }
}
