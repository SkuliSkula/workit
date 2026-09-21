namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>The company's product/material categories; see ProductCategoryEndpoints.</summary>
public interface ICategoriesApi
{
    Task<ApiResult<List<ProductCategoryRow>>> GetCategoriesAsync();
    Task<ApiResult<ProductCategoryRow>> CreateAsync(string name);
    /// <summary>Renames the category and every product/material carrying the old name.</summary>
    Task<ApiResult<ProductCategoryRow>> RenameAsync(Guid id, string name);
    /// <summary>Deletes; when in use, <paramref name="moveTo"/> says where its products and materials go (409 without it).</summary>
    Task<ApiResult> DeleteAsync(Guid id, Guid? moveTo = null);
}

internal sealed class CategoriesApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ICategoriesApi
{
    public async Task<ApiResult<List<ProductCategoryRow>>> GetCategoriesAsync()
    {
        var result = await GetAsync<List<ProductCategoryRow>>("api/categories", "Categories could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<ProductCategoryRow>>.Success(result.Value ?? [])
            : ApiResult<List<ProductCategoryRow>>.Failure(result.ErrorMessage ?? "Categories could not be loaded right now.");
    }

    public Task<ApiResult<ProductCategoryRow>> CreateAsync(string name) =>
        PostForJsonAsync<ProductCategoryRequest, ProductCategoryRow>("api/categories", new ProductCategoryRequest(name), "The category could not be created right now.");

    public Task<ApiResult<ProductCategoryRow>> RenameAsync(Guid id, string name) =>
        PutForJsonAsync<ProductCategoryRequest, ProductCategoryRow>($"api/categories/{id}", new ProductCategoryRequest(name), "The category could not be renamed right now.");

    public Task<ApiResult> DeleteAsync(Guid id, Guid? moveTo = null) =>
        DeleteAsync($"api/categories/{id}{(moveTo is null ? "" : $"?moveTo={moveTo}")}", "The category could not be deleted right now.");
}
