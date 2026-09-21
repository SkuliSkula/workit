namespace Workit.Shared.Models;

/// <summary>Outcome of one Payday ↔ Workit customer sync for a company.</summary>
/// <param name="Fetched">Customers Payday returned.</param>
/// <param name="Added">Payday customers new to Workit.</param>
/// <param name="Updated">Workit customers refreshed from Payday.</param>
/// <param name="Linked">Workit customers matched to Payday by SSN this run.</param>
/// <param name="Pushed">Workit edits that reached Payday this run.</param>
/// <param name="PushFailed">Workit edits Payday still refuses (see each customer's PaydayPushError).</param>
public sealed record PaydayCustomerSyncResult(int Fetched, int Added, int Updated, int Linked, int Pushed, int PushFailed, DateTimeOffset SyncedAt);
