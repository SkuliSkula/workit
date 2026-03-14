using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Workit.Shared.Payday;

public interface IPaydayTokenService
{
    Task<string?> GetTokenAsync();
}

// Singleton — caches the service-level token across requests.
internal sealed class PaydayTokenService : IPaydayTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaydayOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;

    public PaydayTokenService(IHttpClientFactory httpClientFactory, IOptions<PaydayOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken is not null)
            return _cachedToken;

        await _lock.WaitAsync();
        try
        {
            if (_cachedToken is not null)
                return _cachedToken;

            var client = _httpClientFactory.CreateClient("PaydayApi");
            var response = await client.PostAsJsonAsync("auth/token", new
            {
                clientId = _options.ClientId,
                clientSecret = _options.ClientSecret
            });

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<PaydayTokenResponse>();
            _cachedToken = result?.GetToken();
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
