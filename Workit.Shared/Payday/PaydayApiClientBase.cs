using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public abstract class PaydayApiClientBase(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected async Task<ApiResult<T>> GetAsync<T>(string requestUri, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Get, requestUri);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
                return ApiResult<T>.Success(value);
            }
            catch (JsonException ex)
            {
                var preview = json.Length > 300 ? json[..300] + "…" : json;
                return ApiResult<T>.Failure($"Parse error: {ex.Message} | Response: {preview}");
            }
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Failure($"{defaultErrorMessage} ({ex.Message})");
        }
    }

    protected async Task<ApiResult<TResponse>> PutForJsonAsync<TRequest, TResponse>(string requestUri, TRequest payload, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Put, requestUri, payload);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<TResponse>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var value = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
                return ApiResult<TResponse>.Success(value);
            }
            catch (JsonException ex)
            {
                var preview = json.Length > 300 ? json[..300] + "…" : json;
                return ApiResult<TResponse>.Failure($"Parse error: {ex.Message} | Response: {preview}");
            }
        }
        catch (Exception ex)
        {
            return ApiResult<TResponse>.Failure($"{defaultErrorMessage} ({ex.Message})");
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

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var value = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
                return ApiResult<TResponse>.Success(value);
            }
            catch (JsonException ex)
            {
                var preview = json.Length > 300 ? json[..300] + "…" : json;
                return ApiResult<TResponse>.Failure($"Parse error: {ex.Message} | Response: {preview}");
            }
        }
        catch (Exception ex)
        {
            return ApiResult<TResponse>.Failure($"{defaultErrorMessage} ({ex.Message})");
        }
    }

    protected async Task<ApiResult<bool>> DeleteAsync(string requestUri, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Delete, requestUri);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<bool>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            return ApiResult<bool>.Success(true);
        }
        catch (Exception)
        {
            return ApiResult<bool>.Failure(defaultErrorMessage);
        }
    }

    protected async Task<ApiResult<byte[]>> GetBytesAsync(string requestUri, string defaultErrorMessage)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PaydayApi");
            using var request = await CreateRequestAsync(HttpMethod.Get, requestUri);
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return ApiResult<byte[]>.Failure(await ReadErrorAsync(response, defaultErrorMessage));

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return ApiResult<byte[]>.Success(bytes);
        }
        catch (Exception)
        {
            return ApiResult<byte[]>.Failure(defaultErrorMessage);
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
