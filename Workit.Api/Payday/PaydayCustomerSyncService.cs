using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Customers in both directions. Workit → Payday: <see cref="PushAsync"/> writes a
/// customer through on create/edit; a failure leaves the row flagged
/// <c>PaydayPushPending</c> with Payday's message and the next sync retries it.
/// Payday → Workit: <see cref="SyncAsync"/> pushes what is pending, then pulls
/// every Payday customer and upserts by Payday id, else by SSN. Payday wins the
/// billing fields (name, SSN, address, contact); Workit-only fields such as
/// <c>PlanJobsInTasks</c> are never touched. Callers set the company's Payday
/// credentials on the token service first.
/// </summary>
public sealed class PaydayCustomerSyncService(WorkitDbContext db, IPaydayCustomersApi payday, ILogger<PaydayCustomerSyncService> logger)
{
    public sealed record Outcome(PaydayCustomerSyncResult? Result, string? Error);

    /// <summary>The seeded placeholder SSN; never a match key.</summary>
    private const string PlaceholderSsn = "0000000000";

    /// <summary>
    /// Creates or updates the customer in Payday and records the outcome on the
    /// row. Does not save — the caller owns the transaction.
    /// </summary>
    /// <returns><c>true</c> when Payday accepted the write.</returns>
    public async Task<bool> PushAsync(Customer customer, CancellationToken ct)
    {
        ApiResult<PaydayCustomer> result;
        if (customer.PaydayId is Guid paydayId)
        {
            result = await payday.UpdateAsync(paydayId.ToString(), new UpdateCustomerRequest
            {
                Name     = customer.Name,
                Language = NullIfEmpty(customer.Language),
                Address  = customer.Address,
                ZipCode  = customer.ZipCode,
                City     = customer.City,
                Country  = NullIfEmpty(customer.Country),
                Email    = customer.Email,
                Contact  = customer.ContactPerson,
                Comment  = customer.Comment,
                Phone    = customer.Phone,
            });
        }
        else
        {
            // Payday looks an Icelandic SSN up in Registers Iceland and ignores the name;
            // without an SSN it creates a foreign customer from the name.
            var ssn = NormalizeSsn(customer.Ssn);
            result = await payday.CreateAsync(new CreateCustomerRequest
            {
                Ssn      = ssn,
                Name     = customer.Name,
                Language = NullIfEmpty(customer.Language),
                Address  = customer.Address,
                ZipCode  = customer.ZipCode,
                City     = customer.City,
                Country  = NullIfEmpty(customer.Country),
                Email    = customer.Email,
                Contact  = customer.ContactPerson,
                Comment  = customer.Comment,
                Phone    = customer.Phone,
            });
        }

        if (!result.IsSuccess || result.Value is null)
        {
            customer.PaydayPushPending = true;
            customer.PaydayPushError   = result.ErrorMessage ?? "Payday did not accept the customer.";
            logger.LogWarning("Payday customer push failed for {Customer}: {Error}", customer.Name, customer.PaydayPushError);
            return false;
        }

        var sentSsn = NormalizeSsn(customer.Ssn);
        customer.PaydayId          = result.Value.Id;
        customer.PaydayPushPending = false;
        // Payday accepts an SSN it cannot find in Registers Iceland by silently creating the
        // customer without one. That is worth a word: invoices to it carry no kennitala.
        customer.PaydayPushError   = sentSsn is not null && NormalizeSsn(result.Value.Ssn) is null
            ? $"Payday did not recognise the SSN {sentSsn} and saved the customer without one. Check the kennitala in Payday."
            : null;
        customer.PaydaySyncedAt    = DateTimeOffset.UtcNow;
        // Payday may have corrected the name/address from Registers Iceland.
        ApplyBilling(customer, result.Value);
        return true;
    }

    public async Task<Outcome> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var locals = await db.Customers.Where(c => c.CompanyId == companyId).ToListAsync(ct);

        // 1. Workit → Payday: whatever the write-through could not deliver.
        int pushed = 0, pushFailed = 0;
        foreach (var pending in locals.Where(c => c.PaydayPushPending))
        {
            if (await PushAsync(pending, ct)) pushed++; else pushFailed++;
        }
        await db.SaveChangesAsync(ct);

        // 2. Payday → Workit.
        var fetched = new List<PaydayCustomer>();
        for (var page = 1; ; page++)
        {
            var result = await payday.GetAllAsync(page, 100);
            if (!result.IsSuccess || result.Value is null)
                return new Outcome(null, result.ErrorMessage ?? "Payday customers could not be loaded right now.");
            fetched.AddRange(result.Value.Customers);
            if (page >= result.Value.Pages || result.Value.Customers.Count == 0) break;
        }

        var now = DateTimeOffset.UtcNow;
        var byPaydayId = locals.Where(c => c.PaydayId is not null).ToDictionary(c => c.PaydayId!.Value);
        var bySsn = locals
            .Where(c => c.PaydayId is null && IsMatchableSsn(c.Ssn))
            .GroupBy(c => NormalizeSsn(c.Ssn)!)
            .ToDictionary(g => g.Key, g => g.First());
        int added = 0, updated = 0, linked = 0;

        foreach (var remote in fetched)
        {
            if (byPaydayId.TryGetValue(remote.Id, out var local))
            {
                // A push Payday has not accepted yet carries the newer Workit values; keep them.
                if (local.PaydayPushPending) continue;
                ApplyBilling(local, remote);
                local.PaydaySyncedAt = now;
                updated++;
                continue;
            }

            var remoteSsn = NormalizeSsn(remote.Ssn);
            if (remoteSsn is not null && bySsn.TryGetValue(remoteSsn, out local))
            {
                local.PaydayId = remote.Id;
                local.Source   = DataSource.Payday;
                bySsn.Remove(remoteSsn);
                byPaydayId[remote.Id] = local;
                if (!local.PaydayPushPending) ApplyBilling(local, remote);
                local.PaydaySyncedAt = now;
                linked++;
                continue;
            }

            local = new Customer
            {
                CompanyId      = companyId,
                // Workit requires an SSN on save; a foreign Payday customer has none, so it
                // gets the placeholder — which the push treats as "no SSN" again.
                Ssn            = PlaceholderSsn,
                Source         = DataSource.Payday,
                PaydayId       = remote.Id,
                PaydaySyncedAt = now,
                CreatedAt      = now,
                CreatedByName  = "Payday sync",
            };
            ApplyBilling(local, remote);
            db.Customers.Add(local);
            locals.Add(local);
            byPaydayId[remote.Id] = local;
            added++;
        }

        await db.SaveChangesAsync(ct);
        return new Outcome(new PaydayCustomerSyncResult(fetched.Count, added, updated, linked, pushed, pushFailed, now), null);
    }

    /// <summary>Payday owns the billing identity; Workit-only fields are left as they are.</summary>
    private static void ApplyBilling(Customer local, PaydayCustomer remote)
    {
        local.Name          = remote.Name     ?? local.Name;
        local.Ssn           = remote.Ssn      ?? local.Ssn;
        local.Email         = remote.Email    ?? local.Email;
        local.Phone         = remote.Phone    ?? local.Phone;
        local.ContactPerson = remote.Contact  ?? local.ContactPerson;
        local.Address       = remote.Address  ?? local.Address;
        local.ZipCode       = remote.ZipCode  ?? local.ZipCode;
        local.City          = remote.City     ?? local.City;
        local.Country       = remote.Country  ?? local.Country;
        local.Language      = remote.Language ?? local.Language;
        local.Comment       = remote.Comment  ?? local.Comment;
    }

    private static bool IsMatchableSsn(string? ssn) => NormalizeSsn(ssn) is not null;

    /// <summary>Digits only, ten of them; the placeholder and blanks are not SSNs.</summary>
    internal static string? NormalizeSsn(string? ssn)
    {
        if (string.IsNullOrWhiteSpace(ssn)) return null;
        var digits = new string(ssn.Where(char.IsDigit).ToArray());
        return digits.Length == 10 && digits != PlaceholderSsn ? digits : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
