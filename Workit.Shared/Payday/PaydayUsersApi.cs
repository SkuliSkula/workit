using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayUsersApi
{
    Task<ApiResult<PaydayUser>> GetMeAsync();
    Task<ApiResult<List<PaydayUser>>> GetAllAsync();
    Task<ApiResult<PaydayUser>> GetByIdAsync(string userId);
}

internal sealed class PaydayUsersApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayUsersApi
{
    public Task<ApiResult<PaydayUser>> GetMeAsync() =>
        GetAsync<PaydayUser>("users/me", "Failed to fetch current user.");

    public Task<ApiResult<List<PaydayUser>>> GetAllAsync() =>
        GetAsync<List<PaydayUser>>("users", "Failed to fetch users.");

    public Task<ApiResult<PaydayUser>> GetByIdAsync(string userId) =>
        GetAsync<PaydayUser>($"users/{userId}", "Failed to fetch user.");
}
