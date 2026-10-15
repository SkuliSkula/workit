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
