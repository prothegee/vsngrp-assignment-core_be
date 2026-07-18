using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VsngrpCoreBe.Models;

namespace VsngrpCoreBe.Services;

public interface IJwtService
{
    (string AccessToken, int ExpiresInSeconds) IssueAccessToken(Guid accountId, string sessionId);
}

public sealed class JwtService(AppConfig appConfig) : IJwtService
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public (string AccessToken, int ExpiresInSeconds) IssueAccessToken(Guid accountId, string sessionId)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appConfig.JwtSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new Claim("sid", sessionId),
        };

        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var token = new JwtSecurityToken(claims: claims, expires: expiresAt, signingCredentials: credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return (accessToken, (int)AccessTokenLifetime.TotalSeconds);
    }
}
