namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IDrivingApi
{
    Task<ApiResult<List<DrivingEntry>>> GetDrivingEntriesAsync(Guid? jobId = null);
    Task<ApiResult> CreateDrivingEntryAsync(DrivingEntry entry);
    Task<ApiResult> UpdateDrivingEntryAsync(DrivingEntry entry);
    Task<ApiResult> DeleteDrivingEntryAsync(Guid id);
    Task<ApiResult> UpdateDrivingRateAsync(decimal unitPrice);
}

internal sealed class DrivingApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IDrivingApi
{
    public async Task<ApiResult<List<DrivingEntry>>> GetDrivingEntriesAsync(Guid? jobId = null)
    {
        var url = jobId.HasValue ? $"api/driving?jobId={jobId}" : "api/driving";
        var result = await GetAsync<List<DrivingEntry>>(url, "Driving entries could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<DrivingEntry>>.Success(result.Value ?? [])
            : ApiResult<List<DrivingEntry>>.Failure(result.ErrorMessage ?? "Driving entries could not be loaded right now.");
    }

    public Task<ApiResult> CreateDrivingEntryAsync(DrivingEntry entry) =>
        PostAsync("api/driving", entry, "The driving entry could not be saved right now.");

    public Task<ApiResult> UpdateDrivingEntryAsync(DrivingEntry entry) =>
        PutAsync($"api/driving/{entry.Id}", entry, "The driving entry could not be updated right now.");

    public Task<ApiResult> DeleteDrivingEntryAsync(Guid id) =>
        DeleteAsync($"api/driving/{id}", "The driving entry could not be deleted right now.");

    public Task<ApiResult> UpdateDrivingRateAsync(decimal unitPrice) =>
        PutAsync("api/company/driving-rate", new { unitPrice }, "The driving rate could not be updated right now.");
}
