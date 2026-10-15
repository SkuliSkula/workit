using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Stores small pieces of Payday reference data (the company profile, users,
/// lookup lists) as Payday returned them, one row per company and kind, and
/// reads them back when Payday can't be asked. See <see cref="PaydaySnapshot"/>.
/// </summary>
internal static class PaydaySnapshotStore
{
    public const string Company         = "company";
    public const string Users           = "users";
    public const string Me              = "users/me";
    public const string ExpenseAccounts = "expense-accounts";
    public const string PaymentTypes    = "payment-types";
    public const string LedgerAccounts  = "sales-ledger-accounts";
    public static string PensionFunds(PaydayPensionType type) => $"pension-funds/{(int)type}";

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

    /// <summary>
    /// Payday's answer, stored on the way through; when Payday can't be reached,
    /// the last stored copy as a success. Payday's failure only when there is none.
    /// </summary>
    public static async Task<ApiResult<T>> LiveOrStoredAsync<T>(ApiResult<T> live, WorkitDbContext db, Guid companyId, string kind, CancellationToken ct)
    {
        if (live.IsSuccess && live.Value is not null)
        {
            await SaveAsync(db, companyId, kind, live.Value, ct);
            return live;
        }
        return await GetAsync<T>(db, companyId, kind, ct) is { } stored
            ? ApiResult<T>.Success(stored)
            : live;
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
