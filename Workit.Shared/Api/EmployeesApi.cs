using Workit.Shared.Auth;

namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IEmployeesApi
{
    Task<ApiResult<List<Employee>>> GetEmployeesAsync();
    Task<ApiResult> CreateEmployeeAsync(CreateEmployeeUserRequest request);
    Task<ApiResult> UpdateEmployeeAsync(Employee employee);
}

internal sealed class EmployeesApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IEmployeesApi
{
    public async Task<ApiResult<List<Employee>>> GetEmployeesAsync()
    {
        var result = await GetAsync<List<Employee>>(
            "api/employees",
            "Employees could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<Employee>>.Success(result.Value ?? [])
            : ApiResult<List<Employee>>.Failure(result.ErrorMessage ?? "Employees could not be loaded right now.");
    }

    public Task<ApiResult> CreateEmployeeAsync(CreateEmployeeUserRequest request) =>
        PostAsync("api/employees", request, "The employee could not be created right now.");

    public Task<ApiResult> UpdateEmployeeAsync(Employee employee) =>
        PutAsync($"api/employees/{employee.Id}", employee, "The employee could not be updated right now.");
}
