namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IToolsApi
{
    Task<ApiResult<List<Tool>>> GetToolsAsync();
    Task<ApiResult<Tool>> CreateToolAsync(Tool tool);
    Task<ApiResult<Tool>> UpdateToolAsync(Guid id, Tool tool);
    Task<ApiResult> DeleteToolAsync(Guid id);

    // Assignments
    Task<ApiResult<List<ToolAssignment>>> GetToolAssignmentsAsync();
    Task<ApiResult> AssignToolAsync(Guid toolId);
    Task<ApiResult> ReturnToolAsync(Guid toolId);
}

internal sealed class ToolsApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IToolsApi
{
    public async Task<ApiResult<List<Tool>>> GetToolsAsync()
    {
        var result = await GetAsync<List<Tool>>("api/tools", "Tools could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<Tool>>.Success(result.Value ?? [])
            : ApiResult<List<Tool>>.Failure(result.ErrorMessage ?? "Tools could not be loaded right now.");
    }

    public Task<ApiResult<Tool>> CreateToolAsync(Tool tool) =>
        PostForJsonAsync<Tool, Tool>("api/tools", tool, "The tool could not be saved right now.");

    public Task<ApiResult<Tool>> UpdateToolAsync(Guid id, Tool tool) =>
        PutForJsonAsync<Tool, Tool>($"api/tools/{id}", tool, "The tool could not be updated right now.");

    public Task<ApiResult> DeleteToolAsync(Guid id) =>
        DeleteAsync($"api/tools/{id}", "The tool could not be deleted right now.");

    public async Task<ApiResult<List<ToolAssignment>>> GetToolAssignmentsAsync()
    {
        var result = await GetAsync<List<ToolAssignment>>("api/tools/assignments", "Tool assignments could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<ToolAssignment>>.Success(result.Value ?? [])
            : ApiResult<List<ToolAssignment>>.Failure(result.ErrorMessage ?? "Tool assignments could not be loaded right now.");
    }

    public Task<ApiResult> AssignToolAsync(Guid toolId) =>
        PostAsync($"api/tools/{toolId}/assign", null as object, "The tool could not be assigned right now.");

    public Task<ApiResult> ReturnToolAsync(Guid toolId) =>
        PostAsync($"api/tools/{toolId}/return", null as object, "The tool could not be returned right now.");
}
