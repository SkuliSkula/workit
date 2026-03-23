using System.Security.Claims;
using FluentAssertions;
using Workit.Api.Auth;
using Workit.Shared.Auth;

namespace Workit.Tests.Auth;

public class UserContextTests
{
    private static ClaimsPrincipal CreatePrincipal(string role, Guid? userId = null, Guid? companyId = null, Guid? employeeId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new("company_id", (companyId ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Role, role)
        };

        if (employeeId.HasValue)
            claims.Add(new Claim("employee_id", employeeId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // --- ToUserContext ---

    [Fact]
    public void ToUserContext_ExtractsAllClaims()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var principal = CreatePrincipal(WorkitRoles.Employee, userId, companyId, employeeId);

        var ctx = principal.ToUserContext();

        ctx.UserId.Should().Be(userId);
        ctx.CompanyId.Should().Be(companyId);
        ctx.EmployeeId.Should().Be(employeeId);
        ctx.Role.Should().Be(WorkitRoles.Employee);
    }

    [Fact]
    public void ToUserContext_WithoutEmployeeId_SetsNull()
    {
        var principal = CreatePrincipal(WorkitRoles.Owner);
        var ctx = principal.ToUserContext();
        ctx.EmployeeId.Should().BeNull();
    }

    [Fact]
    public void ToUserContext_MissingNameIdentifier_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("company_id", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, WorkitRoles.Owner)
        }, "TestAuth"));

        var act = () => principal.ToUserContext();
        act.Should().Throw<InvalidOperationException>().WithMessage("*NameIdentifier*");
    }

    [Fact]
    public void ToUserContext_MissingCompanyId_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, WorkitRoles.Owner)
        }, "TestAuth"));

        var act = () => principal.ToUserContext();
        act.Should().Throw<InvalidOperationException>().WithMessage("*company_id*");
    }

    // --- IsOwner ---

    [Fact]
    public void IsOwner_OwnerRole_ReturnsTrue()
    {
        var principal = CreatePrincipal(WorkitRoles.Owner);
        principal.IsOwner().Should().BeTrue();
    }

    [Fact]
    public void IsOwner_EmployeeRole_ReturnsFalse()
    {
        var principal = CreatePrincipal(WorkitRoles.Employee);
        principal.IsOwner().Should().BeFalse();
    }

    [Fact]
    public void IsOwner_AdminRole_ReturnsFalse()
    {
        var principal = CreatePrincipal(WorkitRoles.Admin);
        principal.IsOwner().Should().BeFalse();
    }

    // --- IsAdmin ---

    [Fact]
    public void IsAdmin_AdminRole_ReturnsTrue()
    {
        var principal = CreatePrincipal(WorkitRoles.Admin);
        principal.IsAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_OwnerRole_ReturnsFalse()
    {
        var principal = CreatePrincipal(WorkitRoles.Owner);
        principal.IsAdmin().Should().BeFalse();
    }

    // --- IsOwnerOrAdmin ---

    [Fact]
    public void IsOwnerOrAdmin_OwnerRole_ReturnsTrue()
    {
        var principal = CreatePrincipal(WorkitRoles.Owner);
        principal.IsOwnerOrAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsOwnerOrAdmin_AdminRole_ReturnsTrue()
    {
        var principal = CreatePrincipal(WorkitRoles.Admin);
        principal.IsOwnerOrAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsOwnerOrAdmin_EmployeeRole_ReturnsFalse()
    {
        var principal = CreatePrincipal(WorkitRoles.Employee);
        principal.IsOwnerOrAdmin().Should().BeFalse();
    }
}
