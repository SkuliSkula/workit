using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayCustomersApi
{
    Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100);
    Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId);
    Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request);
    Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request);
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
}
