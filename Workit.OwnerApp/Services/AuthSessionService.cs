using System.Text.Json;
using Microsoft.JSInterop;
using Workit.Shared.Api;
using Workit.Shared.Auth;

namespace Workit.OwnerApp.Services;

public sealed class AuthSessionService(IAuthApi authApi, IJSRuntime jsRuntime)
{
    public const string StorageKey = "workit.auth.token";
    public const string SessionStorageKey = "workit.auth.session";

    public async Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request)
    {
        var result = await authApi.LoginAsync(request);
        if (result.IsSuccess && result.Value is not null)
        {
            await SetSessionAsync(result.Value);
        }

        return result;
    }

    public Task<ApiResult<LoginResponse>> RegisterCompanyAsync(RegisterCompanyRequest request) =>
        authApi.RegisterCompanyAsync(request);

    public async Task LogoutAsync()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSException)
        {
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            var token = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task<LoginResponse?> GetSessionAsync()
    {
        try
        {
            var sessionJson = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", SessionStorageKey);
            return string.IsNullOrWhiteSpace(sessionJson)
                ? null
                : JsonSerializer.Deserialize<LoginResponse>(sessionJson);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task SetSessionAsync(LoginResponse session)
    {
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, session.AccessToken);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", SessionStorageKey, JsonSerializer.Serialize(session));
    }
}
