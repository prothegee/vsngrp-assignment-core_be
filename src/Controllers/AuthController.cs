using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VsngrpCoreBe.Models;
using VsngrpCoreBe.Services;

namespace VsngrpCoreBe.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    IAccountService accountService,
    ISessionService sessionService,
    IJwtService jwtService,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshTokenCookieName = "refresh_token";

    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request)
    {
        var (outcome, account) = await accountService.SignupAsync(request.Email, request.Password);
        if (outcome == SignupOutcome.DuplicateEmail || account is null)
        {
            return Conflict(new ErrorResponse { Error = "email_already_registered" });
        }

        return await IssueSessionAsync(account.Id);
    }

    [HttpPost("signin")]
    public async Task<IActionResult> Signin([FromBody] SigninRequest request)
    {
        var (outcome, account) = await accountService.SigninAsync(request.Email, request.Password);
        if (outcome == SigninOutcome.InvalidCredentials || account is null)
        {
            return Unauthorized(new ErrorResponse { Error = "invalid_credentials" });
        }

        return await IssueSessionAsync(account.Id);
    }

    [HttpPost("signout")]
    [Authorize(Policy = "ActiveSession")]
    public async Task<IActionResult> Signout()
    {
        var sessionId = User.FindFirst("sid")?.Value;
        if (!string.IsNullOrEmpty(sessionId))
        {
            await sessionService.DeleteSessionAsync(sessionId);
        }

        Response.Cookies.Delete(RefreshTokenCookieName, BuildCookieOptions(TimeSpan.Zero));

        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken)
            || string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new ErrorResponse { Error = "missing_refresh_token" });
        }

        var sessionId = await sessionService.ConsumeRefreshTokenAsync(refreshToken);
        if (sessionId is null)
        {
            return Unauthorized(new ErrorResponse { Error = "invalid_refresh_token" });
        }

        var accountId = await sessionService.GetAccountIdAsync(sessionId);
        if (accountId is null)
        {
            return Unauthorized(new ErrorResponse { Error = "session_expired" });
        }

        await sessionService.ExtendSessionAsync(sessionId, accountId.Value, SessionService.RefreshTokenLifetime);
        var newRefreshToken = await sessionService.IssueRefreshTokenAsync(sessionId, SessionService.RefreshTokenLifetime);
        Response.Cookies.Append(RefreshTokenCookieName, newRefreshToken, BuildCookieOptions(SessionService.RefreshTokenLifetime));

        var (accessToken, expiresInSeconds) = jwtService.IssueAccessToken(accountId.Value, sessionId);

        return Ok(new AuthResponse { AccessToken = accessToken, ExpiresInSeconds = expiresInSeconds });
    }

    private async Task<IActionResult> IssueSessionAsync(Guid accountId)
    {
        var sessionId = await sessionService.CreateSessionAsync(accountId, SessionService.RefreshTokenLifetime);
        var refreshToken = await sessionService.IssueRefreshTokenAsync(sessionId, SessionService.RefreshTokenLifetime);
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, BuildCookieOptions(SessionService.RefreshTokenLifetime));

        var (accessToken, expiresInSeconds) = jwtService.IssueAccessToken(accountId, sessionId);

        return Ok(new AuthResponse { AccessToken = accessToken, ExpiresInSeconds = expiresInSeconds });
    }

    private CookieOptions BuildCookieOptions(TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Path = "/auth",
        Secure = !environment.IsDevelopment(),
        SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
        Expires = lifetime == TimeSpan.Zero ? DateTimeOffset.UnixEpoch : DateTimeOffset.UtcNow.Add(lifetime),
    };
}
