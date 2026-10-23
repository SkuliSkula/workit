using Workit.Shared.Models;

namespace Workit.Shared.Api;

/// <summary>The console's handle on the Price tracker; see PriceTrackerEndpoints.</summary>
public interface IPriceTrackerApi
{
    Task<ApiResult<PriceTrackerSummary>> GetSummaryAsync();
    Task<ApiResult<List<TrackedItem>>> GetItemsAsync(string? search = null, string? vendor = null);
    Task<ApiResult<TrackedItemHistory>> GetItemAsync(string sku);
    Task<ApiResult<List<TrackedVendor>>> GetVendorsAsync();
    Task<ApiResult<List<PriceListChange>>> GetChangesAsync(int days = 90);
}

internal sealed class PriceTrackerApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPriceTrackerApi
{
    public Task<ApiResult<PriceTrackerSummary>> GetSummaryAsync() =>
        GetAsync<PriceTrackerSummary>("api/prices/summary", "The price summary could not be loaded right now.");

    public Task<ApiResult<List<TrackedItem>>> GetItemsAsync(string? search = null, string? vendor = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add("q=" + Uri.EscapeDataString(search));
        if (!string.IsNullOrWhiteSpace(vendor)) query.Add("vendor=" + Uri.EscapeDataString(vendor));
        return GetAsync<List<TrackedItem>>("api/prices/items" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), "Prices could not be loaded right now.");
    }

    public Task<ApiResult<TrackedItemHistory>> GetItemAsync(string sku) =>
        GetAsync<TrackedItemHistory>("api/prices/items/history?sku=" + Uri.EscapeDataString(sku), "The price history could not be loaded right now.");

    public Task<ApiResult<List<TrackedVendor>>> GetVendorsAsync() =>
        GetAsync<List<TrackedVendor>>("api/prices/vendors", "Sellers could not be loaded right now.");

    public Task<ApiResult<List<PriceListChange>>> GetChangesAsync(int days = 90) =>
        GetAsync<List<PriceListChange>>($"api/prices/changes?days={days}", "Price-list changes could not be loaded right now.");
}
