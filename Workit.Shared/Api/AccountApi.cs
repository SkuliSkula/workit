namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>The signed-in person's own account; see AccountEndpoints.</summary>
public interface IAccountApi
{
    Task<ApiResult<MyAccount>> GetMyAccountAsync();
    Task<ApiResult> UpdateMyAccountAsync(MyAccountUpdate update);
    /// <summary>Removes the sign-in. The employer keeps the employment record and the hours.</summary>
    Task<ApiResult> DeleteMyAccountAsync();
}

internal sealed class AccountApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IAccountApi
{
    public Task<ApiResult<MyAccount>> GetMyAccountAsync() =>
        GetAsync<MyAccount>("api/me", "Your account could not be loaded right now.");

    public Task<ApiResult> UpdateMyAccountAsync(MyAccountUpdate update) =>
        PutAsync("api/me", update, "Your details could not be saved right now.");

    public Task<ApiResult> DeleteMyAccountAsync() =>
        DeleteAsync("api/me", "Your account could not be deleted right now.");
}
