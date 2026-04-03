using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Auth;

public sealed class TokenFactory(IOptions<JwtOptions> jwtOptions)
{
    public LoginResponse CreateToken(AppUser user, Guid? companyIdOverride = null, Guid? employeeIdOverride = null)
    {
        var options = jwtOptions.Value;
        // Use Guid.Empty as the "no company yet" sentinel so the JWT claim is always present
        var companyId = companyIdOverride ?? user.CompanyId ?? Guid.Empty;
        var expires = DateTime.UtcNow.AddMinutes(options.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("company_id", companyId.ToString())
        };

        var employeeId = employeeIdOverride ?? user.EmployeeId;
        if (employeeId is Guid eid)
        {
            claims.Add(new Claim("employee_id", eid.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new LoginResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            UserId = user.Id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Role = user.Role,
            Email = user.Email
        };
    }

    public RefreshToken CreateRefreshToken(Guid userId)
    {
        var options = jwtOptions.Value;
        return new RefreshToken
        {
            UserId = userId,
            Token = GenerateSecureToken(),
            ExpiresUtc = DateTime.UtcNow.AddDays(options.RefreshTokenExpirationDays),
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
