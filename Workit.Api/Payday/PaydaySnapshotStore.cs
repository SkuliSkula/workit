using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Api.Payday;

/// <summary>
/// Stores small pieces of Payday reference data (the company profile, users,
/// lookup lists) as Payday returned them, one row per company and kind, and
/// reads them back when Payday can't be asked. See <see cref="PaydaySnapshot"/>.
/// </summary>
internal static class PaydaySnapshotStore
{
    public const string Company = "company";
    public const string Users   = "users";
    public const string Me      = "users/me";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task SaveAsync<T>(WorkitDbContext db, Guid companyId, string kind, T value, CancellationToken ct)
    {
        using (await PaydaySyncGuard.EnterAsync("snapshots", companyId, ct))
        {
            var row = await db.PaydaySnapshots.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Kind == kind, ct);
            if (row is null)
            {
                row = new PaydaySnapshot { CompanyId = companyId, Kind = kind };
                db.PaydaySnapshots.Add(row);
            }
            row.Json     = JsonSerializer.Serialize(value, Json);
            row.SyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task<T?> GetAsync<T>(WorkitDbContext db, Guid companyId, string kind, CancellationToken ct)
    {
        var json = await db.PaydaySnapshots.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Kind == kind)
            .Select(s => s.Json)
            .FirstOrDefaultAsync(ct);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    }
}
