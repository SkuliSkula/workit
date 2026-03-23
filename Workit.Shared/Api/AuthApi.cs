using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface IAuthApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request);
    Task<ApiResult<LoginResponse>> RefreshAsync(RefreshTokenRequest request);
    Task<ApiResult<LoginResponse>> RegisterCompanyAsync(RegisterCompanyRequest request);
    Task<ApiResult<SetupCompanyResponse>> SetupCompanyAsync(SetupCompanyRequest request);
    Task<ApiResult<List<Company>?>> GetUserCompaniesAsync();
    Task<ApiResult<LoginResponse>> SwitchCompanyAsync(SwitchCompanyRequest request);
    Task<ApiResult<List<AdminCompanyInfo>?>> GetAdminCompaniesAsync();
}

internal sealed class AuthApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IAuthApi
{
    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request) =>
        PostForJsonAsync<LoginRequest, LoginResponse>("api/auth/login", request, "Login failed.");

    public Task<ApiResult<LoginResponse>> RefreshAsync(RefreshTokenRequest request) =>
        PostForJsonAsync<RefreshTokenRequest, LoginResponse>("api/auth/refresh", request, "Token refresh failed.");

    public Task<ApiResult<LoginResponse>> RegisterCompanyAsync(RegisterCompanyRequest request) =>
        PostForJsonAsync<RegisterCompanyRequest, LoginResponse>("api/auth/register-company", request, "Company registration failed.");

    public Task<ApiResult<SetupCompanyResponse>> SetupCompanyAsync(SetupCompanyRequest request) =>
        PostForJsonAsync<SetupCompanyRequest, SetupCompanyResponse>("api/auth/setup-company", request, "Company setup failed.");

    public Task<ApiResult<List<Company>?>> GetUserCompaniesAsync() =>
        GetAsync<List<Company>?>("api/auth/companies", "Could not load companies.");

    public Task<ApiResult<LoginResponse>> SwitchCompanyAsync(SwitchCompanyRequest request) =>
        PostForJsonAsync<SwitchCompanyRequest, LoginResponse>("api/auth/switch-company", request, "Could not switch company.");

    public Task<ApiResult<List<AdminCompanyInfo>?>> GetAdminCompaniesAsync() =>
        GetAsync<List<AdminCompanyInfo>?>("api/auth/admin/companies", "Could not load admin company list.");
}

public sealed class AdminCompanyInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public bool HasPayday { get; set; }
}
