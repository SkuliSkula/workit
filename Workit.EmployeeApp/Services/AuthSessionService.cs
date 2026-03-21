using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Workit.Shared.Api;
using Workit.Shared.Auth;

namespace Workit.EmployeeApp.Services;

public sealed class AuthSessionService(IAuthApi authApi, IJSRuntime jsRuntime)
{
    public const string StorageKey = "workit.auth.token";
    public const string SessionStorageKey = "workit.auth.session";
    public const string RefreshTokenStorageKey = "workit.auth.refreshToken";

    public async Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request)
    {
        var result = await authApi.LoginAsync(request);
        if (result.IsSuccess && result.Value is not null)
        {
            await SetSessionAsync(result.Value);
        }

        return result;
    }

    public async Task<LoginResponse?> GetSessionAsync()
    {
        try
        {
            var token = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);

            if (!string.IsNullOrWhiteSpace(token) && IsTokenExpired(token))
            {
                var refreshed = await TryRefreshAsync();
                if (!refreshed) return null;
            }

            var sessionJson = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", SessionStorageKey);
            return string.IsNullOrWhiteSpace(sessionJson)
                ? null
                : JsonSerializer.Deserialize<LoginResponse>(sessionJson);
        }
        catch (InvalidOperationException) { return null; }
        catch (JSException) { return null; }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenStorageKey);
        }
        catch (InvalidOperationException) { }
        catch (JSException) { }
    }

    private async Task<bool> TryRefreshAsync()
    {
        try
        {
            var refreshToken = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", RefreshTokenStorageKey);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var result = await authApi.RefreshAsync(new RefreshTokenRequest { RefreshToken = refreshToken });
            if (result.IsSuccess && result.Value is not null)
            {
                await SetSessionAsync(result.Value);
                return true;
            }

            await LogoutAsync();
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task SetSessionAsync(LoginResponse session)
    {
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, session.AccessToken);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", SessionStorageKey, JsonSerializer.Serialize(session));
        if (!string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenStorageKey, session.RefreshToken);
        }
    }

    private static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;

            var payload = parts[1];
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64()).UtcDateTime;
                return exp < DateTime.UtcNow.AddSeconds(60);
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}
