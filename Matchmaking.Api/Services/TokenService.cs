using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Matchmaking.Api.Services;

public class TokenService
{
    private readonly string _key;
    private readonly string _issuer;

    public TokenService(IConfiguration config)
    {
        _key = config["Jwt:Key"] ?? "dev-only-change-me-super-secret-key-0123456789abcdef";
        _issuer = config["Jwt:Issuer"] ?? "matchmaking";
    }

    public string CreateToken(string username)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
