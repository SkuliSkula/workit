namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>Hours → Payday payroll; see PayrollEndpoints.</summary>
public interface IPayrollApi
{
    Task<ApiResult<PayrollPreview>> GetPreviewAsync(int year, int month);
    Task<ApiResult<List<PayrollExport>>> GetExportsAsync();
    /// <summary>Uploads the period's timesheet to Payday. Fails with Payday's message.</summary>
    Task<ApiResult<PayrollExport>> SendAsync(int year, int month);
}

internal sealed class PayrollApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPayrollApi
{
    public Task<ApiResult<PayrollPreview>> GetPreviewAsync(int year, int month) =>
        GetAsync<PayrollPreview>($"api/payroll/preview?year={year}&month={month}", "The payroll preview could not be loaded right now.");

    public async Task<ApiResult<List<PayrollExport>>> GetExportsAsync()
    {
        var result = await GetAsync<List<PayrollExport>>("api/payroll/exports", "Payroll exports could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<PayrollExport>>.Success(result.Value ?? [])
            : ApiResult<List<PayrollExport>>.Failure(result.ErrorMessage ?? "Payroll exports could not be loaded right now.");
    }

    public Task<ApiResult<PayrollExport>> SendAsync(int year, int month) =>
        PostForJsonAsync<object, PayrollExport>($"api/payroll/exports?year={year}&month={month}", new { }, "The timesheet could not be sent to Payday right now.");
}
