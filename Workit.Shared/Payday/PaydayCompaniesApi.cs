using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayCompaniesApi
{
    Task<ApiResult<PaydayCompany>> GetMeAsync();
}

internal sealed class PaydayCompaniesApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayCompaniesApi
{
    public Task<ApiResult<PaydayCompany>> GetMeAsync() =>
        GetAsync<PaydayCompany>("companies/me", "Failed to fetch company.");
}
