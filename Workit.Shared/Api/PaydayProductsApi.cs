namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>The console's view of the Payday product cache; see PaydayProductEndpoints.</summary>
public interface IPaydayProductsCacheApi
{
    Task<ApiResult<List<PaydayProductCache>>> GetProductsAsync(bool includeArchived = false);
    /// <summary>Pulls the product list from Payday into the cache. Owner/Admin; fails with Payday's message.</summary>
    Task<ApiResult<PaydayProductSyncResult>> SyncAsync();
    Task<ApiResult<PaydayProductCache>> SetRoleAsync(Guid id, PaydayProductRole role, string? unit = null, string? category = null);
    /// <summary>Plans (dry run) or performs the switch-over of the company's materials to Payday products.</summary>
    Task<ApiResult<MaterialsMigrationResult>> MigrateMaterialsAsync(bool dryRun);
}

internal sealed class PaydayProductsCacheApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayProductsCacheApi
{
    public async Task<ApiResult<List<PaydayProductCache>>> GetProductsAsync(bool includeArchived = false)
    {
        var result = await GetAsync<List<PaydayProductCache>>(
            $"api/payday/products/?includeArchived={(includeArchived ? "true" : "false")}",
            "Payday products could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<PaydayProductCache>>.Success(result.Value ?? [])
            : ApiResult<List<PaydayProductCache>>.Failure(result.ErrorMessage ?? "Payday products could not be loaded right now.");
    }

    public Task<ApiResult<PaydayProductSyncResult>> SyncAsync() =>
        PostForJsonAsync<object, PaydayProductSyncResult>("api/payday/products/sync", new { }, "Payday products could not be synced right now.");

    public Task<ApiResult<MaterialsMigrationResult>> MigrateMaterialsAsync(bool dryRun) =>
        PostForJsonAsync<object, MaterialsMigrationResult>($"api/payday/materials/migrate?dryRun={(dryRun ? "true" : "false")}", new { }, "The materials migration could not be run right now.");

    public Task<ApiResult<PaydayProductCache>> SetRoleAsync(Guid id, PaydayProductRole role, string? unit = null, string? category = null) =>
        PutForJsonAsync<PaydayProductRoleUpdate, PaydayProductCache>($"api/payday/products/{id}/role", new PaydayProductRoleUpdate(role, unit, category), "The product role could not be saved right now.");
}
