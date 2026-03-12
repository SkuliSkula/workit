namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface ICustomersApi
{
    Task<ApiResult<List<Customer>>> GetCustomersAsync();
    Task<ApiResult> CreateCustomerAsync(Customer customer);
    Task<ApiResult> UpdateCustomerAsync(Customer customer);
}

internal sealed class CustomersApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ICustomersApi
{
    public async Task<ApiResult<List<Customer>>> GetCustomersAsync()
    {
        var result = await GetAsync<List<Customer>>(
            "api/customers",
            "Customers could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<Customer>>.Success(result.Value ?? [])
            : ApiResult<List<Customer>>.Failure(result.ErrorMessage ?? "Customers could not be loaded right now.");
    }

    public Task<ApiResult> CreateCustomerAsync(Customer customer) =>
        PostAsync("api/customers", customer, "The customer could not be created right now.");

    public Task<ApiResult> UpdateCustomerAsync(Customer customer) =>
        PutAsync($"api/customers/{customer.Id}", customer, "The customer could not be updated right now.");
}
