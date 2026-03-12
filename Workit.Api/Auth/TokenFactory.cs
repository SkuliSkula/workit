using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Auth;

public sealed class TokenFactory(IOptions<JwtOptions> jwtOptions)
{
    public LoginResponse CreateToken(AppUser user)
    {
        var options = jwtOptions.Value;
        var expires = DateTime.UtcNow.AddMinutes(options.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("company_id", user.CompanyId.ToString())
        };

        if (user.EmployeeId is Guid employeeId)
        {
            claims.Add(new Claim("employee_id", employeeId.ToString()));
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
            CompanyId = user.CompanyId,
            EmployeeId = user.EmployeeId,
            Role = user.Role,
            Email = user.Email
        };
    }
}
