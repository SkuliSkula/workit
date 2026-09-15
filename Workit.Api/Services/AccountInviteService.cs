using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Api.Services;

/// <summary>Which welcome email an invite belongs to — owners and employees sign in in different places.</summary>
internal enum InviteKind
{
    Owner,
    Employee
}

/// <summary>
/// Issues the one-time link a new account uses to choose its own password.
///
/// New accounts are created with an unguessable random password that is never
/// stored, shown or sent, so the only way in is the emailed link. This replaced
/// mailing the plaintext password, which left it sitting in the recipient's
/// inbox indefinitely.
/// </summary>
internal interface IAccountInviteService
{
    Task SendInviteAsync(string email, string displayName, InviteKind kind, CancellationToken ct = default);

    /// <summary>A password no one holds, for an account that must be invited before it can be used.</summary>
    string CreateUnusablePassword();
}

internal sealed class AccountInviteService(
    WorkitDbContext db,
    IEmailService emailService,
    IConfiguration configuration,
    ILogger<AccountInviteService> logger) : IAccountInviteService
{
    /// <summary>
    /// Invites last far longer than a password reset: a new hire may not open
    /// their email for days, and an expired invite is a support call.
    /// </summary>
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public string CreateUnusablePassword() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public async Task SendInviteAsync(string email, string displayName, InviteKind kind, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Demo logins are filed with Apple App Review; their password must never move.
        if (DemoDataSeeder.IsProtectedAccount(normalizedEmail))
        {
            logger.LogInformation("Skipped an invite for the protected demo account {Email}.", normalizedEmail);
            return;
        }

        // Supersede any outstanding link so only the newest one works.
        var outstanding = await db.PasswordResetTokens
            .Where(t => t.Email == normalizedEmail && !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var token in outstanding) token.Used = true;

        var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Email     = normalizedEmail,
            Token     = rawToken,
            ExpiresAt = DateTime.UtcNow.Add(InviteLifetime)
        });
        await db.SaveChangesAsync(ct);

        // "new=1" only changes the wording on the landing page. The page redeems
        // the token the same way either way, so an older deploy of the site that
        // ignores the parameter still works.
        var appUrl = configuration["App:Url"]?.TrimEnd('/') ?? "https://help.workit.is";
        var setupUrl = $"{appUrl}/reset-password?token={rawToken}&new=1";

        if (kind == InviteKind.Owner)
            await emailService.SendOwnerInviteAsync(displayName, normalizedEmail, setupUrl);
        else
            await emailService.SendEmployeeInviteAsync(displayName, normalizedEmail, setupUrl);
    }
}
