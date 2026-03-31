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
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Failure($"Could not reach the server. ({ex.Message})");
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Failure($"{defaultErrorMessage} ({ex.GetType().Name}: {ex.Message})");
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

    protected async Task<ApiResult> PatchAsync<T>(string requestUri, T payload, string defaultErrorMessage)
    {
        return await SendAsync(HttpMethod.Patch, requestUri, payload, defaultErrorMessage);
    }

    protected async Task<ApiResult<TResponse>> PostForJsonAsync<TRequest, TResponse>(string requestUri, TRequest payload, string defaultErrorMessage)
    {
        return await SendForJsonAsync<TRequest, TResponse>(HttpMethod.Post, requestUri, payload, defaultErrorMessage);
    }

    protected async Task<ApiResult<TResponse>> PutForJsonAsync<TRequest, TResponse>(string requestUri, TRequest payload, string defaultErrorMessage)
    {
        return await SendForJsonAsync<TRequest, TResponse>(HttpMethod.Put, requestUri, payload, defaultErrorMessage);
    }

    protected async Task<ApiResult> DeleteAsync(string requestUri, string defaultErrorMessage)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Delete, requestUri);
            using var response = await httpClient.SendAsync(request);
            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadErrorAsync(response, defaultErrorMessage));
        }
        catch (HttpRequestException ex)
        {
            return ApiResult.Failure($"Could not reach the server. ({ex.Message})");
        }
        catch (Exception ex)
        {
            return ApiResult.Failure($"{defaultErrorMessage} ({ex.GetType().Name}: {ex.Message})");
        }
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
        catch (HttpRequestException ex)
        {
            return ApiResult.Failure($"Could not reach the server. ({ex.Message})");
        }
        catch (Exception ex)
        {
            return ApiResult.Failure($"{defaultErrorMessage} ({ex.GetType().Name}: {ex.Message})");
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
        catch (HttpRequestException ex)
        {
            return ApiResult<TResponse>.Failure($"Could not reach the server. ({ex.Message})");
        }
        catch (Exception ex)
        {
            return ApiResult<TResponse>.Failure($"{defaultErrorMessage} ({ex.GetType().Name}: {ex.Message})");
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
