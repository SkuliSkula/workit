using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayPayrollApi
{
    /// <summary>Uploads one period's hours; Payday reports how many employees it recognised.</summary>
    Task<ApiResult<TimesheetUploadResult>> UploadTimesheetAsync(List<TimesheetEntry> entries);
}

internal sealed class PaydayPayrollApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayPayrollApi
{
    public Task<ApiResult<TimesheetUploadResult>> UploadTimesheetAsync(List<TimesheetEntry> entries) =>
        PostForJsonAsync<List<TimesheetEntry>, TimesheetUploadResult>("payroll/upload/timesheet", entries, "The timesheet could not be uploaded to Payday right now.");
}
