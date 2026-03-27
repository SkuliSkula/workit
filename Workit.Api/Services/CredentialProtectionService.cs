using Microsoft.AspNetCore.DataProtection;

namespace Workit.Api.Services;

public interface ICredentialProtectionService
{
    string Protect(string plaintext);
    string? Unprotect(string? ciphertext);
}

/// <summary>
/// Encrypts and decrypts sensitive credentials (e.g. Payday ClientSecret) stored in the database.
/// Uses ASP.NET Core Data Protection so keys are managed outside the codebase.
/// </summary>
public sealed class CredentialProtectionService : ICredentialProtectionService
{
    private const string Purpose = "Workit.Payday.Credentials.v1";
    private readonly IDataProtector _protector;

    public CredentialProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
            return null;

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch
        {
            // If decryption fails the value was likely stored unencrypted (migration scenario).
            // Return as-is so existing plain-text values keep working until re-saved.
            return ciphertext;
        }
    }
}
