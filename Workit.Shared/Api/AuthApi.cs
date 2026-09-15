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
    Task<ApiResult> CreateOwnerAsync(CreateOwnerRequest request);
    Task<ApiResult<List<AdminOwnerInfo>?>> GetAdminOwnersAsync();
    Task<ApiResult> UpdateOwnerAsync(Guid id, UpdateOwnerRequest request);
    Task<ApiResult> DeleteOwnerAsync(Guid id);
    Task<ApiResult<LoginResponse>> SetupOwnerCompanyAsync(OwnerSetupCompanyRequest request);
    /// <summary>Returns a re-issued session — every other device is signed out.</summary>
    Task<ApiResult<LoginResponse>> ChangePasswordAsync(ChangePasswordRequest request);
    Task<ApiResult> ForgotPasswordAsync(ForgotPasswordRequest request);
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

    public Task<ApiResult> CreateOwnerAsync(CreateOwnerRequest request) =>
        PostAsync("api/auth/admin/create-owner", request, "Could not create owner account.");

    public Task<ApiResult<List<AdminOwnerInfo>?>> GetAdminOwnersAsync() =>
        GetAsync<List<AdminOwnerInfo>?>("api/auth/admin/owners", "Could not load owners list.");

    public Task<ApiResult> UpdateOwnerAsync(Guid id, UpdateOwnerRequest request) =>
        PutAsync($"api/auth/admin/owners/{id}", request, "Could not update owner account.");

    public Task<ApiResult> DeleteOwnerAsync(Guid id) =>
        DeleteAsync($"api/auth/admin/owners/{id}", "Could not delete owner account.");

    public Task<ApiResult<LoginResponse>> SetupOwnerCompanyAsync(OwnerSetupCompanyRequest request) =>
        PostForJsonAsync<OwnerSetupCompanyRequest, LoginResponse>("api/auth/owner/setup-company", request, "Company setup failed.");

    public Task<ApiResult<LoginResponse>> ChangePasswordAsync(ChangePasswordRequest request) =>
        PostForJsonAsync<ChangePasswordRequest, LoginResponse>("api/auth/change-password", request, "Could not change your password.");

    public Task<ApiResult> ForgotPasswordAsync(ForgotPasswordRequest request) =>
        PostAsync("api/auth/forgot-password", request, "Could not send a reset link.");
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
