namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>The console's handle on the customers two-way sync; see PaydayCustomerEndpoints.</summary>
public interface IPaydayCustomerSyncApi
{
    /// <summary>Pushes pending Workit edits, then pulls Payday's customer list. Owner/Admin; fails with Payday's message.</summary>
    Task<ApiResult<PaydayCustomerSyncResult>> SyncAsync();
    /// <summary>Retries one customer's write to Payday (create or update).</summary>
    Task<ApiResult<Customer>> PushAsync(Guid customerId);
}

internal sealed class PaydayCustomerSyncApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayCustomerSyncApi
{
    public Task<ApiResult<PaydayCustomerSyncResult>> SyncAsync() =>
        PostForJsonAsync<object, PaydayCustomerSyncResult>("api/payday/customers/sync", new { }, "Customers could not be synced with Payday right now.");

    public Task<ApiResult<Customer>> PushAsync(Guid customerId) =>
        PostForJsonAsync<object, Customer>($"api/payday/customers/{customerId}/push", new { }, "The customer could not be sent to Payday right now.");
}
