using System.Text.Json;
using Microsoft.JSInterop;
using Workit.Shared.Api;
using Workit.Shared.Auth;

namespace Workit.EmployeeApp.Services;

public sealed class AuthSessionService(IAuthApi authApi, IJSRuntime jsRuntime)
{
    public const string StorageKey = "workit.auth.token";
    public const string SessionStorageKey = "workit.auth.session";

    public async Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request)
    {
        var result = await authApi.LoginAsync(request);
        if (result.IsSuccess && result.Value is not null)
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, result.Value.AccessToken);
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", SessionStorageKey, JsonSerializer.Serialize(result.Value));
        }

        return result;
    }

    public async Task<LoginResponse?> GetSessionAsync()
    {
        var sessionJson = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", SessionStorageKey);
        return string.IsNullOrWhiteSpace(sessionJson)
            ? null
            : JsonSerializer.Deserialize<LoginResponse>(sessionJson);
    }

    public async Task LogoutAsync()
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
    }
}
