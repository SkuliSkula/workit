namespace Workit.Shared.Payday;

using Workit.Shared.Api;
using Workit.Shared.Models;

/// <summary>
/// The console's handle on Workit's stored copy of the Payday invoices: how fresh
/// it is, and a refresh that pulls Payday's whole list into it.
/// </summary>
public interface IInvoiceSyncApi
{
    Task<ApiResult<PaydayInvoiceSyncStatus>> GetStatusAsync();
    Task<ApiResult<PaydayInvoiceSyncResult>> SyncAsync();
}

internal sealed class InvoiceSyncProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IInvoiceSyncApi
{
    public Task<ApiResult<PaydayInvoiceSyncStatus>> GetStatusAsync() =>
        GetAsync<PaydayInvoiceSyncStatus>("api/payday/invoices/sync", "Could not tell when invoices were last synced.");

    public Task<ApiResult<PaydayInvoiceSyncResult>> SyncAsync() =>
        PostForJsonAsync<object, PaydayInvoiceSyncResult>("api/payday/invoices/sync", new { }, "Invoices could not be synced from Payday right now.");
}

/// <summary>The console's handle on Workit's stored copy of the Payday expenses.</summary>
public interface IExpenseSyncApi
{
    Task<ApiResult<PaydayExpenseSyncStatus>> GetStatusAsync();
    Task<ApiResult<PaydayExpenseSyncResult>> SyncAsync();
}

internal sealed class ExpenseSyncProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IExpenseSyncApi
{
    public Task<ApiResult<PaydayExpenseSyncStatus>> GetStatusAsync() =>
        GetAsync<PaydayExpenseSyncStatus>("api/payday/expenses/sync", "Could not tell when expenses were last synced.");

    public Task<ApiResult<PaydayExpenseSyncResult>> SyncAsync() =>
        PostForJsonAsync<object, PaydayExpenseSyncResult>("api/payday/expenses/sync", new { }, "Expenses could not be synced from Payday right now.");
}
