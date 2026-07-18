using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VsngrpCoreBe.Models;
using VsngrpCoreBe.Services;

namespace VsngrpCoreBe.Tests.Unit;

public sealed class JwtServiceTests
{
    private const string Secret = "unit-test-jwt-secret-value-not-real";

    private static JwtService CreateService() => new(new AppConfig { JwtSecret = Secret });

    [Fact]
    public void IssueAccessToken_ContainsAccountIdAndSessionIdClaims()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        const string sessionId = "session-abc";

        var (accessToken, expiresInSeconds) = service.IssueAccessToken(accountId, sessionId);

        var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(accountId.ToString(), jwtToken.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(sessionId, jwtToken.Claims.First(claim => claim.Type == "sid").Value);
        Assert.Equal((int)JwtService.AccessTokenLifetime.TotalSeconds, expiresInSeconds);
    }

    [Fact]
    public void IssueAccessToken_ExpiresApproximatelyAfterConfiguredLifetime()
    {
        var service = CreateService();

        var (accessToken, _) = service.IssueAccessToken(Guid.NewGuid(), "session-abc");

        var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var expectedExpiry = DateTime.UtcNow.Add(JwtService.AccessTokenLifetime);
        Assert.True(Math.Abs((jwtToken.ValidTo - expectedExpiry).TotalSeconds) < 5);
    }

    [Fact]
    public void IssueAccessToken_SignatureValidatesWithSameSecret()
    {
        var service = CreateService();
        var (accessToken, _) = service.IssueAccessToken(Guid.NewGuid(), "session-abc");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(accessToken, validationParameters, out _);
        Assert.NotNull(principal.FindFirst("sid"));
    }

    [Fact]
    public void IssueAccessToken_SignatureRejectedWithDifferentSecret()
    {
        var service = CreateService();
        var (accessToken, _) = service.IssueAccessToken(Guid.NewGuid(), "session-abc");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-secret-value")),
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(accessToken, validationParameters, out _));
    }
}
