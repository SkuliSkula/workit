using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Reads Workit's customers that are linked to Payday back out in Payday's own
/// shape, so the console pages built against Payday's customer list (the
/// invoice pickers, the Customers page) read from Workit. Kept current by
/// <see cref="PaydayCustomerSyncService"/>, both directions.
/// </summary>
internal static class PaydayCustomerStore
{
    /// <summary>The seeded placeholder for a customer with no SSN; Payday has none for it.</summary>
    private const string PlaceholderSsn = "0000000000";

    public static async Task<PaydayCustomersResponse> ListAsync(WorkitDbContext db, Guid companyId, int page, int perPage, CancellationToken ct)
    {
        var query = Linked(db, companyId);
        var total = await query.CountAsync(ct);
        perPage = Math.Clamp(perPage, 1, 500);
        page    = Math.Max(1, page);
        var rows = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * perPage).Take(perPage)
            .ToListAsync(ct);
        return new PaydayCustomersResponse
        {
            Customers = rows.Select(ToPayday).ToList(),
            Page      = page,
            PerPage   = perPage,
            Total     = total,
            Pages     = Math.Max(1, (int)Math.Ceiling(total / (double)perPage)),
        };
    }

    public static async Task<PaydayCustomer?> GetAsync(WorkitDbContext db, Guid companyId, Guid paydayId, CancellationToken ct) =>
        await Linked(db, companyId).FirstOrDefaultAsync(c => c.PaydayId == paydayId, ct) is { } customer
            ? ToPayday(customer)
            : null;

    private static IQueryable<Customer> Linked(WorkitDbContext db, Guid companyId) =>
        db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.PaydayId != null);

    private static PaydayCustomer ToPayday(Customer c) => new()
    {
        Id       = c.PaydayId!.Value,
        Ssn      = string.IsNullOrWhiteSpace(c.Ssn) || c.Ssn == PlaceholderSsn ? null : c.Ssn,
        Name     = c.Name,
        Email    = NullIfEmpty(c.Email),
        Phone    = NullIfEmpty(c.Phone),
        Contact  = NullIfEmpty(c.ContactPerson),
        Address  = NullIfEmpty(c.Address),
        ZipCode  = NullIfEmpty(c.ZipCode),
        City     = NullIfEmpty(c.City),
        Country  = NullIfEmpty(c.Country),
        Language = NullIfEmpty(c.Language),
        Comment  = NullIfEmpty(c.Comment),
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
