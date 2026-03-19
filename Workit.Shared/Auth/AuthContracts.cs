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
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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
