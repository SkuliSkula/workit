namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface ITimeEntriesApi
{
    Task<ApiResult<List<TimeEntry>>> GetTimeEntriesAsync(DateOnly from);
    Task<ApiResult> CreateTimeEntryAsync(TimeEntry timeEntry);
}

internal sealed class TimeEntriesApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ITimeEntriesApi
{
    public async Task<ApiResult<List<TimeEntry>>> GetTimeEntriesAsync(DateOnly from)
    {
        var result = await GetAsync<List<TimeEntry>>(
            $"api/timeentries?from={from:yyyy-MM-dd}",
            "Time entries could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<TimeEntry>>.Success(result.Value ?? [])
            : ApiResult<List<TimeEntry>>.Failure(result.ErrorMessage ?? "Time entries could not be loaded right now.");
    }

    public Task<ApiResult> CreateTimeEntryAsync(TimeEntry timeEntry) =>
        PostAsync("api/timeentries", timeEntry, "The time entry could not be saved right now.");
}
