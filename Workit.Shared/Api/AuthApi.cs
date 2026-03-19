using Workit.Shared.Auth;

namespace Workit.Shared.Api;

public interface IAuthApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request);
    Task<ApiResult<LoginResponse>> RegisterCompanyAsync(RegisterCompanyRequest request);
    Task<ApiResult<SetupCompanyResponse>> SetupCompanyAsync(SetupCompanyRequest request);
}

internal sealed class AuthApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IAuthApi
{
    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request) =>
        PostForJsonAsync<LoginRequest, LoginResponse>("api/auth/login", request, "Login failed.");

    public Task<ApiResult<LoginResponse>> RegisterCompanyAsync(RegisterCompanyRequest request) =>
        PostForJsonAsync<RegisterCompanyRequest, LoginResponse>("api/auth/register-company", request, "Company registration failed.");

    public Task<ApiResult<SetupCompanyResponse>> SetupCompanyAsync(SetupCompanyRequest request) =>
        PostForJsonAsync<SetupCompanyRequest, SetupCompanyResponse>("api/auth/setup-company", request, "Company setup failed.");
}
