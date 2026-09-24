namespace Workit.Shared.Models;

/// <summary>
/// The signed-in person's own account, as the apps show it on Settings.
/// Everything after <see cref="City"/> is read-only: the employer sets the
/// trade and the company, the email is the sign-in identity, and
/// <see cref="CanDelete"/> is false for the last owner of a company.
/// </summary>
public sealed record MyAccount(
    string Name,
    string Email,
    string Phone,
    string Address,
    string ZipCode,
    string City,
    string Trade,
    string CompanyName,
    string Role,
    bool   CanDelete);

/// <summary>What the apps may change: the person's own name and contact details.</summary>
public sealed record MyAccountUpdate(
    string? Name,
    string? Phone,
    string? Address,
    string? ZipCode,
    string? City);
