using Workit.Shared.Api;
using Workit.Shared.Payday;

namespace Workit.OwnerApp.Services;

/// <summary>
/// Initializes the Payday token service with the current company's credentials.
/// Call <see cref="InitializeAsync"/> at the start of any Payday-related page.
/// </summary>
public sealed class PaydayCredentialsInitializer(ICompanyApi companyApi, IPaydayTokenService tokenService)
{
    private bool _initialized;

    public async Task<bool> InitializeAsync()
    {
        if (_initialized) return true;

        var companyResult = await companyApi.GetCompanyAsync();
        if (!companyResult.IsSuccess || companyResult.Value is null)
            return false;

        var company = companyResult.Value;
        if (!string.IsNullOrWhiteSpace(company.PaydayClientId) && !string.IsNullOrWhiteSpace(company.PaydayClientSecret))
        {
            tokenService.SetCredentials(company.PaydayClientId, company.PaydayClientSecret);
            _initialized = true;
            return true;
        }

        // No credentials configured — Payday calls will use fallback (appsettings) or fail gracefully
        _initialized = true;
        return true;
    }
}
