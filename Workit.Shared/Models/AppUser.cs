namespace Workit.Shared.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Null for owners that have not yet completed company onboarding.
    /// Use Guid.Empty as a safe sentinel — a null CompanyId in the JWT maps to Guid.Empty.
    /// </summary>
    public Guid? CompanyId { get; set; }

    public Guid? EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
