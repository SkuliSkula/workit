namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IMaterialsApi
{
    Task<ApiResult<List<Material>>> GetMaterialsAsync(string? category = null, bool activeOnly = false);
    Task<ApiResult<Material>> CreateMaterialAsync(Material material);
    Task<ApiResult<Material>> UpdateMaterialAsync(Guid id, Material material);
    Task<ApiResult> DeleteMaterialAsync(Guid id);

    Task<ApiResult<List<MaterialUsage>>> GetMaterialUsageAsync(Guid? jobId = null);
    Task<ApiResult<MaterialUsage>> LogMaterialUsageAsync(MaterialUsage usage);
    Task<ApiResult> MarkMaterialUsageInvoicedAsync(MarkInvoicedRequest request);
    Task<ApiResult> MarkMaterialUsageUninvoicedAsync(MarkUninvoicedRequest request);
}

internal sealed class MaterialsApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IMaterialsApi
{
    public async Task<ApiResult<List<Material>>> GetMaterialsAsync(string? category = null, bool activeOnly = false)
    {
        var url = "api/materials";
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (activeOnly) qs.Add("activeOnly=true");
        if (qs.Count > 0) url += "?" + string.Join("&", qs);

        var result = await GetAsync<List<Material>>(url, "Materials could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<Material>>.Success(result.Value ?? [])
            : ApiResult<List<Material>>.Failure(result.ErrorMessage ?? "Materials could not be loaded right now.");
    }

    public Task<ApiResult<Material>> CreateMaterialAsync(Material material) =>
        PostForJsonAsync<Material, Material>("api/materials", material, "The material could not be saved right now.");

    public Task<ApiResult<Material>> UpdateMaterialAsync(Guid id, Material material) =>
        PutForJsonAsync<Material, Material>($"api/materials/{id}", material, "The material could not be updated right now.");

    public Task<ApiResult> DeleteMaterialAsync(Guid id) =>
        DeleteAsync($"api/materials/{id}", "The material could not be deleted right now.");

    public async Task<ApiResult<List<MaterialUsage>>> GetMaterialUsageAsync(Guid? jobId = null)
    {
        var url = jobId is null ? "api/materials/usage" : $"api/materials/usage?jobId={jobId}";
        var result = await GetAsync<List<MaterialUsage>>(url, "Material usage could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<MaterialUsage>>.Success(result.Value ?? [])
            : ApiResult<List<MaterialUsage>>.Failure(result.ErrorMessage ?? "Material usage could not be loaded right now.");
    }

    public Task<ApiResult<MaterialUsage>> LogMaterialUsageAsync(MaterialUsage usage) =>
        PostForJsonAsync<MaterialUsage, MaterialUsage>("api/materials/usage", usage, "Material usage could not be logged right now.");

    public Task<ApiResult> MarkMaterialUsageInvoicedAsync(MarkInvoicedRequest request) =>
        PostAsync("api/materials/usage/mark-invoiced", request, "Material usage could not be marked as invoiced.");

    public Task<ApiResult> MarkMaterialUsageUninvoicedAsync(MarkUninvoicedRequest request) =>
        PostAsync("api/materials/usage/mark-uninvoiced", request, "Material usage could not be unmarked as invoiced.");
}
