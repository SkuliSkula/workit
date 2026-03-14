using System.Net.Http.Headers;
using System.Net.Http.Json;
using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public abstract class PaydayApiClientBase(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
{
    protected async Task<ApiResult<T>> GetAsync<T>(string requestUri, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Get, requestUri);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            var value = await response.Content.ReadFromJsonAsync<T>();
            return ApiResult<T>.Success(value);
        }
        catch (Exception)
        {
            return ApiResult<T>.Failure(defaultErrorMessage);
        }
    }

    protected async Task<ApiResult<TResponse>> PostForJsonAsync<TRequest, TResponse>(string requestUri, TRequest payload, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Post, requestUri, payload);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<TResponse>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            var value = await response.Content.ReadFromJsonAsync<TResponse>();
            return ApiResult<TResponse>.Success(value);
        }
        catch (Exception)
        {
            return ApiResult<TResponse>.Failure(defaultErrorMessage);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string requestUri, object? payload = null)
    {
        var request = new HttpRequestMessage(method, requestUri);
        var token = await tokenService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (payload is not null)
            request.Content = JsonContent.Create(payload);

        return request;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string defaultErrorMessage)
    {
        var errorMessage = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(errorMessage) ? defaultErrorMessage : errorMessage;
    }
}
