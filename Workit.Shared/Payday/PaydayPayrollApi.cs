using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayPayrollApi
{
    Task<ApiResult<bool>> UploadTimesheetAsync(List<TimesheetEntry> entries);
}

internal sealed class PaydayPayrollApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayPayrollApi
{
    public Task<ApiResult<bool>> UploadTimesheetAsync(List<TimesheetEntry> entries) =>
        PostForJsonAsync<List<TimesheetEntry>, bool>("payroll/upload/timesheet", entries, "Failed to upload timesheet.");
}
