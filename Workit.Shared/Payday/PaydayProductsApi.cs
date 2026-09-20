using Workit.Shared.Api;

namespace Workit.Shared.Payday;

/// <summary>
/// Payday's product list. Workit never edits or deletes products — the owner
/// does that in Payday — and creates them only during the materials migration.
/// </summary>
public interface IPaydayProductsApi
{
    /// <summary>One page of products; Payday's page size cap is not documented, 100 works.</summary>
    Task<ApiResult<PaydayProductsResponse>> GetAllAsync(int page = 1, int perPage = 100, string orderBy = "sku", string order = "asc", string? query = null);
    Task<ApiResult<PaydayProduct>> GetByIdAsync(Guid id);
    Task<ApiResult<PaydayProduct>> GetBySkuAsync(string sku);
    Task<ApiResult<PaydayProductMovementsResponse>> GetMovementsAsync(Guid id, int page = 1, int perPage = 100);
    Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync();
    /// <summary>Creates a product; only the materials migration calls this.</summary>
    Task<ApiResult<PaydayProduct>> CreateAsync(CreateProductRequest request);
}

internal sealed class PaydayProductsApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayProductsApi
{
    public Task<ApiResult<PaydayProductsResponse>> GetAllAsync(int page = 1, int perPage = 100, string orderBy = "sku", string order = "asc", string? query = null)
    {
        var qs = new List<string> { $"page={page}", $"perpage={perPage}", $"orderBy={orderBy}", $"order={order}" };
        if (!string.IsNullOrWhiteSpace(query)) qs.Add($"query={Uri.EscapeDataString(query)}");
        return GetAsync<PaydayProductsResponse>($"products/?{string.Join("&", qs)}", "Payday products could not be loaded right now.");
    }

    public Task<ApiResult<PaydayProduct>> GetByIdAsync(Guid id) =>
        GetAsync<PaydayProduct>($"products/{id}", "The Payday product could not be loaded right now.");

    public Task<ApiResult<PaydayProduct>> GetBySkuAsync(string sku) =>
        GetAsync<PaydayProduct>($"products/sku/?sku={Uri.EscapeDataString(sku)}", "The Payday product could not be loaded right now.");

    public Task<ApiResult<PaydayProductMovementsResponse>> GetMovementsAsync(Guid id, int page = 1, int perPage = 100) =>
        GetAsync<PaydayProductMovementsResponse>($"products/{id}/movements?page={page}&perpage={perPage}", "Stock movements could not be loaded right now.");

    public Task<ApiResult<List<PaydayLedgerAccount>>> GetSalesLedgerAccountsAsync() =>
        GetAsync<List<PaydayLedgerAccount>>("products/salesLedgerAccounts", "Ledger accounts could not be loaded right now.");

    public Task<ApiResult<PaydayProduct>> CreateAsync(CreateProductRequest request) =>
        PostForJsonAsync<CreateProductRequest, PaydayProduct>("products", request, "The product could not be created in Payday right now.");
}
