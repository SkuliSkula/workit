namespace Workit.Shared.Models;

/// <summary>Outcome of one Payday → Workit invoice sync for a company.</summary>
/// <param name="Fetched">Invoices Payday returned.</param>
/// <param name="Added">Payday invoices new to Workit.</param>
/// <param name="Updated">Stored invoices refreshed from Payday.</param>
/// <param name="Removed">Stored invoices Payday no longer has (deleted drafts).</param>
public sealed record PaydayInvoiceSyncResult(int Fetched, int Added, int Updated, int Removed, DateTimeOffset SyncedAt);

/// <summary>How fresh Workit's copy of the company's Payday invoices is.</summary>
public sealed record PaydayInvoiceSyncStatus(int Stored, DateTimeOffset? LastSyncedAt);
