namespace Workit.Shared.Models;

/// <summary>Outcome of one Payday → Workit expense sync for a company.</summary>
/// <param name="Fetched">Expenses Payday returned.</param>
/// <param name="Added">Payday expenses new to Workit.</param>
/// <param name="Updated">Stored expenses refreshed from Payday.</param>
/// <param name="Removed">Stored expenses Payday no longer has and nothing was billed from.</param>
public sealed record PaydayExpenseSyncResult(int Fetched, int Added, int Updated, int Removed, DateTimeOffset SyncedAt);

/// <summary>How fresh Workit's copy of the company's Payday expenses is.</summary>
public sealed record PaydayExpenseSyncStatus(int Stored, DateTimeOffset? LastSyncedAt);
