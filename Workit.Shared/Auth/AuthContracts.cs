namespace Workit.Shared.Auth;

public static class WorkitRoles
{
    public const string Admin = "Admin";
    public const string Owner = "Owner";
    public const string Employee = "Employee";
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Identifies the client app — e.g. "owner_web", "ios". Used for analytics only.</summary>
    public string? Source { get; set; }
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Guid.Empty means the owner has not yet completed company onboarding.</summary>
    public Guid CompanyId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class RegisterCompanyRequest
{
    public Models.Company Company { get; set; } = new();
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPassword { get; set; } = string.Empty;
}

public sealed class CreateEmployeeUserRequest
{
    public Models.Employee Employee { get; set; } = new();
    public string Password { get; set; } = string.Empty;
}

public sealed class SetupCompanyRequest
{
    public Models.Company Company { get; set; } = new();
    public string OwnerEmail { get; set; } = string.Empty;
}

public sealed class SetupCompanyResponse
{
    public string OwnerEmail { get; set; } = string.Empty;
    public string GeneratedPassword { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class SwitchCompanyRequest
{
    public Guid CompanyId { get; set; }
}

/// <summary>Admin creates an owner account with no company yet.</summary>
public sealed class CreateOwnerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Owner self-provisions their first company (manual or from Payday).</summary>
public sealed class OwnerSetupCompanyRequest
{
    public Models.Company Company { get; set; } = new();
    /// <summary>Optional — persist Payday credentials at the same time.</summary>
    public string? PaydayClientId { get; set; }
    public string? PaydayClientSecret { get; set; }
}

/// <summary>Admin overview row for a single owner account.</summary>
public sealed class AdminOwnerInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool HasCompany { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}
