using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayEmployeesApi
{
    Task<ApiResult<List<PaydayEmployee>>> GetAllAsync();
    Task<ApiResult<PaydayEmployee>> GetByIdAsync(string employeeId);
    Task<ApiResult<PaydayEmployee>> CreateAsync(CreateEmployeeRequest request);
    Task<ApiResult<PaydayEmployee>> UpdateAsync(string employeeId, UpdateEmployeeRequest request);
    Task<ApiResult<bool>> DeleteAsync(string employeeId);
}

internal sealed class PaydayEmployeesApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayEmployeesApi
{
    public Task<ApiResult<List<PaydayEmployee>>> GetAllAsync() =>
        GetAsync<List<PaydayEmployee>>("payroll/employees", "Failed to fetch employees.");

    public Task<ApiResult<PaydayEmployee>> GetByIdAsync(string employeeId) =>
        GetAsync<PaydayEmployee>($"payroll/employees/{employeeId}", "Failed to fetch employee.");

    public Task<ApiResult<PaydayEmployee>> CreateAsync(CreateEmployeeRequest request) =>
        PostForJsonAsync<CreateEmployeeRequest, PaydayEmployee>("payroll/employees", request, "Failed to create employee.");

    public Task<ApiResult<PaydayEmployee>> UpdateAsync(string employeeId, UpdateEmployeeRequest request) =>
        PutForJsonAsync<UpdateEmployeeRequest, PaydayEmployee>($"payroll/employees/{employeeId}", request, "Failed to update employee.");

    public Task<ApiResult<bool>> DeleteAsync(string employeeId) =>
        DeleteAsync($"payroll/employees/{employeeId}", "Failed to delete employee.");
}
