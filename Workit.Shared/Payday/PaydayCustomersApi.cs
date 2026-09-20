using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayCustomersApi
{
    Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100);
    Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId);
    Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request);
    Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request);
    /// <summary>The customer's invoices, newest first.</summary>
    Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25);
    /// <summary>The customer's receivables ledger for a date range, with running balance.</summary>
    Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100);
}

internal sealed class PaydayCustomersApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayCustomersApi
{
    public Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100) =>
        GetAsync<PaydayCustomersResponse>($"customers?page={page}&perpage={perPage}", "Failed to fetch customers.");

    public Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId) =>
        GetAsync<PaydayCustomer>($"customers/{customerId}", "Failed to fetch customer.");

    public Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request) =>
        PostForJsonAsync<CreateCustomerRequest, PaydayCustomer>("customers", request, "Failed to create customer.");

    public Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request) =>
        PutForJsonAsync<UpdateCustomerRequest, PaydayCustomer>($"customers/{customerId}", request, "Failed to update customer.");

    public Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25) =>
        GetAsync<PaydayInvoicesResponse>($"customers/{customerId}/invoice?perpage={perPage}&page={page}&include=lines", "The customer's invoices could not be loaded right now.");

    public Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100) =>
        GetAsync<PaydayAccountStatement>($"customers/{customerId}/accountStatement?dateFrom={dateFrom}&dateTo={dateTo}&perpage={perPage}&page={page}", "The account statement could not be loaded right now.");
}
