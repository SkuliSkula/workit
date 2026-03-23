using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Workit.Shared.Payday;

public interface IPaydayTokenService
{
    /// <summary>
    /// Gets a Payday bearer token using the active company credentials.
    /// </summary>
    Task<string?> GetTokenAsync();

    /// <summary>
    /// Sets the credentials to use for Payday API calls in this scope.
    /// Call this once per request/circuit with the current company's credentials.
    /// </summary>
    void SetCredentials(string clientId, string clientSecret);

    /// <summary>
    /// Tests a set of credentials and returns the token if successful, null otherwise.
    /// Does not cache.
    /// </summary>
    Task<string?> TestCredentialsAsync(string clientId, string clientSecret);
}

// Scoped — each Blazor circuit / request gets its own instance.
// A static ConcurrentDictionary caches tokens by clientId to avoid unnecessary auth calls.
internal sealed class PaydayTokenService : IPaydayTokenService
{
    private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new();
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(25); // refresh before Payday's expiry

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaydayOptions _fallbackOptions;

    private string? _clientId;
    private string? _clientSecret;

    public PaydayTokenService(IHttpClientFactory httpClientFactory, IOptions<PaydayOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _fallbackOptions = options.Value;
    }

    public void SetCredentials(string clientId, string clientSecret)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task<string?> GetTokenAsync()
    {
        var clientId = _clientId ?? _fallbackOptions.ClientId;
        var clientSecret = _clientSecret ?? _fallbackOptions.ClientSecret;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        // Check cache
        if (TokenCache.TryGetValue(clientId, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Token;

        // Fetch new token
        var token = await FetchTokenAsync(clientId, clientSecret);
        if (token is not null)
        {
            TokenCache[clientId] = new CachedToken(token, DateTime.UtcNow.Add(TokenLifetime));
        }
        else
        {
            // Remove stale cache entry on failure
            TokenCache.TryRemove(clientId, out _);
        }

        return token;
    }

    public async Task<string?> TestCredentialsAsync(string clientId, string clientSecret)
    {
        return await FetchTokenAsync(clientId, clientSecret);
    }

    private async Task<string?> FetchTokenAsync(string clientId, string clientSecret)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("PaydayApi");
            var response = await client.PostAsJsonAsync("auth/token", new
            {
                clientId,
                clientSecret
            });

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<PaydayTokenResponse>();
            return result?.GetToken();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Removes a cached token so the next call re-authenticates.</summary>
    public static void InvalidateCache(string clientId) => TokenCache.TryRemove(clientId, out _);

    private sealed record CachedToken(string Token, DateTime ExpiresUtc);
}
