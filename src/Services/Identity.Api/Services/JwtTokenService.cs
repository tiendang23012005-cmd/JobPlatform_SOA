using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Identity.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Services;

public class JwtTokenService(IConfiguration config)
{
    public (string Token, DateTime ExpiresAt) Create(User user, string fullName)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(config.GetValue("Jwt:ExpiresMinutes", 60));
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("name", fullName),
            new("role", user.Role),
            new("email_verified", user.IsVerified ? "true" : "false"),
            new("jti", Guid.NewGuid().ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            config["Jwt:Issuer"], config["Jwt:Audience"], claims, now, expires,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
