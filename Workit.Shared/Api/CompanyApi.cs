namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface ICompanyApi
{
    Task<ApiResult<Company?>> GetCompanyAsync();
    Task<ApiResult> CreateCompanyAsync(Company company);
}

internal sealed class CompanyApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), ICompanyApi
{
    public Task<ApiResult<Company?>> GetCompanyAsync() =>
        GetAsync<Company?>("api/company", "The company profile could not be loaded right now.");

    public Task<ApiResult> CreateCompanyAsync(Company company) =>
        PostAsync("api/companies", company, "The company could not be created right now.");
}
