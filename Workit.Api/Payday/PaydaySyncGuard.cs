using System.Collections.Concurrent;

namespace Workit.Api.Payday;

/// <summary>
/// What every Payday → Workit copy (invoices, expenses) needs to stay right:
/// one writer at a time per company and kind, so the background sync, a page's
/// refresh and a write-through never add the same record twice; and a short
/// memory of records stored straight from a Payday write, because Payday's
/// lists lag its writes by about a minute and a list fetched in that window
/// would put the old values back.
/// </summary>
internal static class PaydaySyncGuard
{
    /// <summary>How long a list can still show a record as it was before a write.</summary>
    internal static readonly TimeSpan ListLag = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<(string Kind, Guid CompanyId), SemaphoreSlim> Gates = new();
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> DirectSaves = new();

    /// <summary>Waits for this company's turn; dispose the result to hand it on.</summary>
    public static async Task<IDisposable> EnterAsync(string kind, Guid companyId, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd((kind, companyId), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(gate);
    }

    /// <summary>Records that Workit just stored (or dropped) this Payday record from a write.</summary>
    public static void SavedDirectly(Guid paydayId) => DirectSaves[paydayId] = DateTimeOffset.UtcNow;

    /// <summary>True when the record was stored from a write recently enough that a list fetched at <paramref name="listedAt"/> may predate it.</summary>
    public static bool SavedSince(Guid paydayId, DateTimeOffset listedAt)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var old in DirectSaves.Where(d => d.Value < now - ListLag - ListLag).ToList())
            DirectSaves.TryRemove(old);
        return DirectSaves.TryGetValue(paydayId, out var at) && at > listedAt - ListLag;
    }

    /// <summary>Payday sends plain dates; Iceland is UTC all year, and Postgres timestamptz needs the kind spelled out.</summary>
    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? Utc(DateTime? value) => value is DateTime v ? Utc(v) : null;

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) gate.Release();
        }
    }
}
