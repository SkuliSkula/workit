namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface ICompanyApi
{
    Task<ApiResult<Company?>> GetCompanyAsync();
    Task<ApiResult> CreateCompanyAsync(Company company);
    Task<ApiResult> UpdateDrivingRateAsync(decimal unitPrice);
    Task<ApiResult> UpdateStandardHoursAsync(decimal standardHoursPerDay);
    Task<ApiResult> UpdatePaydayCredentialsAsync(string? clientId, string? clientSecret);
    Task<ApiResult<PaydayTestResult?>> TestPaydayCredentialsAsync(string clientId, string clientSecret);
}

internal sealed class CompanyApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ICompanyApi
{
    public Task<ApiResult<Company?>> GetCompanyAsync() =>
        GetAsync<Company?>("api/company", "The company profile could not be loaded right now.");

    public Task<ApiResult> CreateCompanyAsync(Company company) =>
        PostAsync("api/companies", company, "The company could not be created right now.");

    public Task<ApiResult> UpdateDrivingRateAsync(decimal unitPrice) =>
        PutAsync("api/company/driving-rate", new { unitPrice }, "The driving rate could not be updated right now.");

    public Task<ApiResult> UpdateStandardHoursAsync(decimal standardHoursPerDay) =>
        PutAsync("api/company/standard-hours", new { standardHoursPerDay }, "The standard hours setting could not be updated right now.");

    public Task<ApiResult> UpdatePaydayCredentialsAsync(string? clientId, string? clientSecret) =>
        PutAsync("api/company/payday-credentials", new { clientId, clientSecret }, "Could not update Payday credentials.");

    public Task<ApiResult<PaydayTestResult?>> TestPaydayCredentialsAsync(string clientId, string clientSecret) =>
        PostForJsonAsync<object, PaydayTestResult?>("api/company/payday-test", new { clientId, clientSecret }, "Could not test Payday connection.");
}

public sealed class PaydayTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
