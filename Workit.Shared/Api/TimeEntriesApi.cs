namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface ITimeEntriesApi
{
    Task<ApiResult<List<TimeEntry>>> GetTimeEntriesAsync(DateOnly? from = null, DateOnly? to = null, Guid? employeeId = null, Guid? jobId = null);
    Task<ApiResult> CreateTimeEntryAsync(TimeEntry timeEntry);
}

internal sealed class TimeEntriesApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ITimeEntriesApi
{
    public async Task<ApiResult<List<TimeEntry>>> GetTimeEntriesAsync(DateOnly? from = null, DateOnly? to = null, Guid? employeeId = null, Guid? jobId = null)
    {
        var query = new List<string>();

        if (from is not null)   query.Add($"from={from:yyyy-MM-dd}");
        if (to is not null)     query.Add($"to={to:yyyy-MM-dd}");
        if (employeeId is not null) query.Add($"employeeId={employeeId}");
        if (jobId is not null)  query.Add($"jobId={jobId}");

        var url = query.Count > 0
            ? $"api/timeentries?{string.Join("&", query)}"
            : "api/timeentries";

        var result = await GetAsync<List<TimeEntry>>(url, "Time entries could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<TimeEntry>>.Success(result.Value ?? [])
            : ApiResult<List<TimeEntry>>.Failure(result.ErrorMessage ?? "Time entries could not be loaded right now.");
    }

    public Task<ApiResult> CreateTimeEntryAsync(TimeEntry timeEntry) =>
        PostAsync("api/timeentries", timeEntry, "The time entry could not be saved right now.");
}
