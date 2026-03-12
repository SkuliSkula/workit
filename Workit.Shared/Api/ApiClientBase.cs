using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Workit.Shared.Api;

public abstract class ApiClientBase(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
{
    protected async Task<ApiResult<T>> GetAsync<T>(string requestUri, string defaultErrorMessage)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, requestUri);
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<T>.Failure(await ReadErrorAsync(response, defaultErrorMessage));
            }

            var value = await response.Content.ReadFromJsonAsync<T>();
            return ApiResult<T>.Success(value);
        }
        catch (Exception)
        {
            return ApiResult<T>.Failure(defaultErrorMessage);
        }
    }

    protected async Task<ApiResult> PostAsync<T>(string requestUri, T payload, string defaultErrorMessage)
    {
        return await SendAsync(HttpMethod.Post, requestUri, payload, defaultErrorMessage);
    }

    protected async Task<ApiResult> PutAsync<T>(string requestUri, T payload, string defaultErrorMessage)
    {
        return await SendAsync(HttpMethod.Put, requestUri, payload, defaultErrorMessage);
    }

    protected async Task<ApiResult<TResponse>> PostForJsonAsync<TRequest, TResponse>(string requestUri, TRequest payload, string defaultErrorMessage)
    {
        return await SendForJsonAsync<TRequest, TResponse>(HttpMethod.Post, requestUri, payload, defaultErrorMessage);
    }

    private async Task<ApiResult> SendAsync<T>(HttpMethod method, string requestUri, T payload, string defaultErrorMessage)
    {
        try
        {
            using var request = await CreateRequestAsync(method, requestUri, payload);
            using var response = await httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return ApiResult.Success();
            }

            return ApiResult.Failure(await ReadErrorAsync(response, defaultErrorMessage));
        }
        catch (Exception)
        {
            return ApiResult.Failure(defaultErrorMessage);
        }
    }

    private async Task<ApiResult<TResponse>> SendForJsonAsync<TRequest, TResponse>(HttpMethod method, string requestUri, TRequest payload, string defaultErrorMessage)
    {
        try
        {
            using var request = await CreateRequestAsync(method, requestUri, payload);
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<TResponse>.Failure(await ReadErrorAsync(response, defaultErrorMessage));
            }

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
        var accessToken = await accessTokenAccessor.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string defaultErrorMessage)
    {
        var errorMessage = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(errorMessage) ? defaultErrorMessage : errorMessage;
    }
}
