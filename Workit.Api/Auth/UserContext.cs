using System.Security.Claims;

namespace Workit.Api.Auth;

public sealed class UserContext
{
    public Guid UserId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? EmployeeId { get; init; }
    public string Role { get; init; } = string.Empty;
}

public static class ClaimsPrincipalExtensions
{
    public static UserContext ToUserContext(this ClaimsPrincipal user)
    {
        var userId = GetRequiredGuid(user, ClaimTypes.NameIdentifier);
        var companyId = GetRequiredGuid(user, "company_id");
        var employeeId = GetOptionalGuid(user, "employee_id");
        var role = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        return new UserContext
        {
            UserId = userId,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Role = role
        };
    }

    public static bool IsOwner(this ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(ClaimTypes.Role), Workit.Shared.Auth.WorkitRoles.Owner, StringComparison.Ordinal);

    public static bool IsAdmin(this ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(ClaimTypes.Role), Workit.Shared.Auth.WorkitRoles.Admin, StringComparison.Ordinal);

    public static bool IsOwnerOrAdmin(this ClaimsPrincipal user) =>
        IsOwner(user) || IsAdmin(user);

    private static Guid GetRequiredGuid(ClaimsPrincipal user, string claimType)
    {
        var rawValue = user.FindFirstValue(claimType);
        if (!Guid.TryParse(rawValue, out var value))
        {
            throw new InvalidOperationException($"Missing required claim '{claimType}'.");
        }

        return value;
    }

    private static Guid? GetOptionalGuid(ClaimsPrincipal user, string claimType)
    {
        var rawValue = user.FindFirstValue(claimType);
        return Guid.TryParse(rawValue, out var value) ? value : null;
    }
}
