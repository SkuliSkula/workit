using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Payday → Workit for payroll employees. Payday's employees are stored in
/// <c>PaydayEmployees</c> and the employee routes answer from there; an
/// employee Payday no longer lists is archived, never deleted. Each Workit
/// employee whose SSN matches one is linked (<see cref="Employee.PaydayId"/>) —
/// linked only: their name, contact details and app account stay as they are.
/// Callers set the company's Payday credentials on the token service first.
/// </summary>
public sealed class PaydayEmployeeSyncService(
    WorkitDbContext db,
    IPaydayEmployeesApi employees)
{
    public sealed record Outcome(PaydayEmployeeSyncResult? Result, string? Error);

    private const string Kind = "employees";

    public async Task<Outcome> SyncAsync(Guid companyId, CancellationToken ct)
    {
        var result = await employees.GetAllAsync();
        if (!result.IsSuccess || result.Value is null)
            return new Outcome(null, result.ErrorMessage ?? "Payday employees could not be loaded right now.");

        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var now = DateTimeOffset.UtcNow;
            var stored = await db.PaydayEmployees.Where(e => e.CompanyId == companyId).ToListAsync(ct);
            var byPaydayId = stored.ToDictionary(e => e.PaydayId);
            int added = 0, updated = 0, archived = 0;

            foreach (var remote in result.Value.DistinctBy(e => e.Id))
            {
                if (byPaydayId.TryGetValue(remote.Id, out var local))
                {
                    Apply(local, remote, now);
                    updated++;
                }
                else
                {
                    local = new PaydayEmployeeCache { CompanyId = companyId, PaydayId = remote.Id };
                    Apply(local, remote, now);
                    db.PaydayEmployees.Add(local);
                    stored.Add(local);
                    added++;
                }
            }

            // Payday's employee list comes in one piece, so "not listed" means gone.
            var listed = result.Value.Select(e => e.Id).ToHashSet();
            foreach (var gone in stored.Where(e => !listed.Contains(e.PaydayId) && !e.Archived))
            {
                gone.Archived = true;
                gone.SyncedAt = now;
                archived++;
            }

            var linked = await LinkAsync(companyId, stored, ct);
            await db.SaveChangesAsync(ct);
            return new Outcome(new PaydayEmployeeSyncResult(result.Value.Count, added, updated, archived, linked, now), null);
        }
    }

    /// <summary>Stores one employee as Payday returned it (opening it, or a write to Payday).</summary>
    public async Task SaveAsync(Guid companyId, PaydayEmployee remote, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var stored = await db.PaydayEmployees.Where(e => e.CompanyId == companyId).ToListAsync(ct);
            var local = stored.FirstOrDefault(e => e.PaydayId == remote.Id);
            if (local is null)
            {
                local = new PaydayEmployeeCache { CompanyId = companyId, PaydayId = remote.Id };
                db.PaydayEmployees.Add(local);
                stored.Add(local);
            }
            Apply(local, remote, DateTimeOffset.UtcNow);
            await LinkAsync(companyId, stored, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Marks an employee Payday deleted as archived.</summary>
    public async Task ArchiveAsync(Guid companyId, Guid paydayId, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync(Kind, companyId, ct))
        {
            var local = await db.PaydayEmployees.FirstOrDefaultAsync(e => e.CompanyId == companyId && e.PaydayId == paydayId, ct);
            if (local is null) return;
            local.Archived = true;
            local.SyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Links unlinked Workit employees to the Payday employee with their SSN. Returns how many.</summary>
    private async Task<int> LinkAsync(Guid companyId, List<PaydayEmployeeCache> stored, CancellationToken ct)
    {
        var bySsn = stored
            .Where(e => !e.Archived && Normalize(e.Ssn) is not null)
            .GroupBy(e => Normalize(e.Ssn)!)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().PaydayId);
        if (bySsn.Count == 0) return 0;

        var workit = await db.Employees.Where(e => e.CompanyId == companyId).ToListAsync(ct);
        var taken = workit.Where(e => e.PaydayId is not null).Select(e => e.PaydayId!.Value).ToHashSet();
        var linked = 0;
        foreach (var employee in workit.Where(e => e.PaydayId is null))
        {
            if (Normalize(employee.Ssn) is not { } ssn || !bySsn.TryGetValue(ssn, out var paydayId) || !taken.Add(paydayId))
                continue;
            employee.PaydayId = paydayId;
            linked++;
        }
        return linked;
    }

    private static void Apply(PaydayEmployeeCache local, PaydayEmployee remote, DateTimeOffset now)
    {
        local.Ssn      = remote.Ssn;
        local.Name     = remote.Name;
        local.Address  = remote.Address;
        local.Zip      = remote.Zip;
        local.City     = remote.City;
        local.Mobile   = remote.Mobile;
        local.Email    = remote.Email;
        local.Active   = remote.Active;
        local.Archived = false;
        local.SyncedAt = now;
    }

    internal static PaydayEmployee ToPayday(PaydayEmployeeCache e) => new()
    {
        Id      = e.PaydayId,
        Ssn     = e.Ssn,
        Name    = e.Name,
        Address = e.Address,
        Zip     = e.Zip,
        City    = e.City,
        Mobile  = e.Mobile,
        Email   = e.Email,
        Active  = e.Active,
    };

    /// <summary>Digits only, ten of them; the placeholder and blanks are not SSNs.</summary>
    private static string? Normalize(string? ssn) => PaydayCustomerSyncService.NormalizeSsn(ssn);
}
