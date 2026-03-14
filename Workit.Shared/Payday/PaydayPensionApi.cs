using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayPensionApi
{
    Task<ApiResult<List<PaydayPensionFund>>> GetByTypeAsync(PaydayPensionType type);
}

internal sealed class PaydayPensionApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayPensionApi
{
    public Task<ApiResult<List<PaydayPensionFund>>> GetByTypeAsync(PaydayPensionType type) =>
        GetAsync<List<PaydayPensionFund>>($"payroll/pension/funds/{(int)type}", "Failed to fetch pension funds.");
}
