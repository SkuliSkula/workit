namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IJobsApi
{
    Task<ApiResult<List<Job>>> GetJobsAsync();
    Task<ApiResult> CreateJobAsync(Job job);
    Task<ApiResult> UpdateJobAsync(Job job);
    Task<ApiResult> UpdateKanbanStatusAsync(Guid jobId, KanbanStatus status, string? waitingReason);
}

internal sealed class JobsApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IJobsApi
{
    public async Task<ApiResult<List<Job>>> GetJobsAsync()
    {
        var result = await GetAsync<List<Job>>(
            "api/jobs",
            "Jobs could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<Job>>.Success(result.Value ?? [])
            : ApiResult<List<Job>>.Failure(result.ErrorMessage ?? "Jobs could not be loaded right now.");
    }

    public Task<ApiResult> CreateJobAsync(Job job) =>
        PostAsync("api/jobs", job, "The job could not be created right now.");

    public Task<ApiResult> UpdateJobAsync(Job job) =>
        PutAsync($"api/jobs/{job.Id}", job, "The job could not be updated right now.");

    public Task<ApiResult> UpdateKanbanStatusAsync(Guid jobId, KanbanStatus status, string? waitingReason) =>
        PatchAsync($"api/jobs/{jobId}/kanban-status",
            new { Status = status, WaitingReason = waitingReason },
            "The job status could not be updated right now.");
}
