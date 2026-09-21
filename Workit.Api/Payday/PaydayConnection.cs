using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Loads a company's Payday credentials into the request-scoped token service
/// when the company has any. For routes that work with or without Payday
/// (customer create/edit), where <see cref="Endpoints.PaydayCredentialsFilter"/>
/// would be too strict.
/// </summary>
internal static class PaydayConnection
{
    /// <returns><c>true</c> when the company is connected and the token service is ready to use.</returns>
    public static async Task<bool> TryConnectAsync(WorkitDbContext db, ICredentialProtectionService protection, IPaydayTokenService tokens, Guid companyId, CancellationToken ct)
    {
        var credentials = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.PaydayClientId, c.PaydayClientSecret })
            .FirstOrDefaultAsync(ct);

        var clientId = protection.Unprotect(credentials?.PaydayClientId);
        var secret   = protection.Unprotect(credentials?.PaydayClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return false;

        tokens.SetCredentials(clientId, secret);
        return true;
    }
}
