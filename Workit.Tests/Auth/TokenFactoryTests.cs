using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Workit.Api.Auth;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Auth;

public class TokenFactoryTests
{
    private readonly JwtOptions _jwtOptions = new()
    {
        SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        ExpirationMinutes = 30,
        RefreshTokenExpirationDays = 14
    };

    private TokenFactory CreateFactory() => new(Options.Create(_jwtOptions));

    private AppUser CreateUser(string role = WorkitRoles.Owner, Guid? employeeId = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Email = "test@example.com",
        Role = role,
        EmployeeId = employeeId
    };

    [Fact]
    public void CreateToken_ReturnsValidJwt()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(result.AccessToken).Should().BeTrue();
    }

    [Fact]
    public void CreateToken_SetsCorrectResponseFields()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user);

        result.UserId.Should().Be(user.Id);
        result.CompanyId.Should().Be(user.CompanyId ?? Guid.Empty);
        result.Email.Should().Be(user.Email);
        result.Role.Should().Be(user.Role);
        result.EmployeeId.Should().BeNull();
    }

    [Fact]
    public void CreateToken_WithEmployeeId_IncludesEmployeeClaim()
    {
        var factory = CreateFactory();
        var employeeId = Guid.NewGuid();
        var user = CreateUser(WorkitRoles.Employee, employeeId);

        var result = factory.CreateToken(user);

        result.EmployeeId.Should().Be(employeeId);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        token.Claims.Should().Contain(c => c.Type == "employee_id" && c.Value == employeeId.ToString());
    }

    [Fact]
    public void CreateToken_WithoutEmployeeId_OmitsEmployeeClaim()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        token.Claims.Should().NotContain(c => c.Type == "employee_id");
    }

    [Fact]
    public void CreateToken_ContainsExpectedClaims()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        token.Claims.Should().Contain(c => c.Type == "company_id" && c.Value == (user.CompanyId ?? Guid.Empty).ToString());
        token.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == user.Role);
    }

    [Fact]
    public void CreateToken_WithCompanyIdOverride_UsesOverrideCompanyId()
    {
        var factory = CreateFactory();
        var user = CreateUser();
        var overrideCompanyId = Guid.NewGuid();

        var result = factory.CreateToken(user, overrideCompanyId);

        result.CompanyId.Should().Be(overrideCompanyId);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        token.Claims.Should().Contain(c => c.Type == "company_id" && c.Value == overrideCompanyId.ToString());
    }

    [Fact]
    public void CreateToken_WithNullOverride_UsesUserCompanyId()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user, null);

        result.CompanyId.Should().Be(user.CompanyId ?? Guid.Empty);
    }

    [Fact]
    public void CreateToken_SetsIssuerAndAudience()
    {
        var factory = CreateFactory();
        var user = CreateUser();

        var result = factory.CreateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        token.Issuer.Should().Be("TestIssuer");
        token.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void CreateToken_ExpiresInConfiguredMinutes()
    {
        var factory = CreateFactory();
        var user = CreateUser();
        var before = DateTime.UtcNow;

        var result = factory.CreateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        var expectedExpiry = before.AddMinutes(_jwtOptions.ExpirationMinutes);
        token.ValidTo.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateRefreshToken_ReturnsValidToken()
    {
        var factory = CreateFactory();
        var userId = Guid.NewGuid();

        var refreshToken = factory.CreateRefreshToken(userId);

        refreshToken.UserId.Should().Be(userId);
        refreshToken.Token.Should().NotBeNullOrWhiteSpace();
        refreshToken.ExpiresUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void CreateRefreshToken_ExpiresInConfiguredDays()
    {
        var factory = CreateFactory();
        var before = DateTime.UtcNow;

        var refreshToken = factory.CreateRefreshToken(Guid.NewGuid());

        var expectedExpiry = before.AddDays(_jwtOptions.RefreshTokenExpirationDays);
        refreshToken.ExpiresUtc.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateRefreshToken_GeneratesUniqueTokens()
    {
        var factory = CreateFactory();
        var userId = Guid.NewGuid();

        var token1 = factory.CreateRefreshToken(userId);
        var token2 = factory.CreateRefreshToken(userId);

        token1.Token.Should().NotBe(token2.Token);
    }
}
