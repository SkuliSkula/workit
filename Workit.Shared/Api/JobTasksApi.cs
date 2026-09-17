namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>Tasks inside jobs; see JobTaskEndpoints for who may do what.</summary>
public interface IJobTasksApi
{
    /// <summary>All tasks the caller may see, optionally for one job and/or one status.</summary>
    Task<ApiResult<List<JobTask>>> GetTasksAsync(Guid? jobId = null, JobTaskStatus? status = null);
    Task<ApiResult<JobTask>> CreateTaskAsync(Guid jobId, JobTask task);
    Task<ApiResult<JobTask>> UpdateTaskAsync(JobTask task);
    Task<ApiResult<JobTask>> MarkDoneAsync(Guid id);
    Task<ApiResult<JobTask>> ReopenAsync(Guid id);
    /// <summary>Refused (409) when the task has hours on an invoice.</summary>
    Task<ApiResult> DeleteTaskAsync(Guid id);
}

internal sealed class JobTasksApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IJobTasksApi
{
    public async Task<ApiResult<List<JobTask>>> GetTasksAsync(Guid? jobId = null, JobTaskStatus? status = null)
    {
        var query = new List<string>();
        if (jobId is not null) query.Add($"jobId={jobId}");
        if (status is not null) query.Add($"status={(int)status}");
        var url = "api/tasks" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var result = await GetAsync<List<JobTask>>(url, "Tasks could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<JobTask>>.Success(result.Value ?? [])
            : ApiResult<List<JobTask>>.Failure(result.ErrorMessage ?? "Tasks could not be loaded right now.");
    }

    public Task<ApiResult<JobTask>> CreateTaskAsync(Guid jobId, JobTask task) =>
        PostForJsonAsync<JobTask, JobTask>($"api/jobs/{jobId}/tasks", task, "The task could not be created right now.");

    public Task<ApiResult<JobTask>> UpdateTaskAsync(JobTask task) =>
        PutForJsonAsync<JobTask, JobTask>($"api/tasks/{task.Id}", task, "The task could not be saved right now.");

    public Task<ApiResult<JobTask>> MarkDoneAsync(Guid id) =>
        PostForJsonAsync<object, JobTask>($"api/tasks/{id}/done", new { }, "The task could not be marked done right now.");

    public Task<ApiResult<JobTask>> ReopenAsync(Guid id) =>
        PostForJsonAsync<object, JobTask>($"api/tasks/{id}/reopen", new { }, "The task could not be reopened right now.");

    public Task<ApiResult> DeleteTaskAsync(Guid id) =>
        DeleteAsync($"api/tasks/{id}", "The task could not be deleted right now.");
}
