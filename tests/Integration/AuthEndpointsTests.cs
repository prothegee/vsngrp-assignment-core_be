using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace VsngrpCoreBe.Tests.Integration;

public sealed class AuthEndpointsTests(AuthEndpointsTestFixture fixture) : IClassFixture<AuthEndpointsTestFixture>
{
    [Fact]
    public async Task Signup_NewEmail_ReturnsAccessTokenAndRefreshCookie()
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/signup", new { email = UniqueEmail(), password = "SignupPassw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.NotNull(ExtractRefreshToken(response));
    }

    [Fact]
    public async Task Signup_DuplicateEmail_ReturnsConflict()
    {
        var email = UniqueEmail();
        await fixture.Client.PostAsJsonAsync("/auth/signup", new { email, password = "FirstPassw0rd!" });

        var response = await fixture.Client.PostAsJsonAsync("/auth/signup", new { email, password = "SecondPassw0rd!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Signup_MissingPassword_ReturnsBadRequest()
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/signup", new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signin_WrongPassword_ReturnsUnauthorized()
    {
        var email = UniqueEmail();
        await fixture.Client.PostAsJsonAsync("/auth/signup", new { email, password = "CorrectPassw0rd!" });

        var response = await fixture.Client.PostAsJsonAsync("/auth/signin", new { email, password = "WrongPassw0rd!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signin_CorrectPassword_ReturnsAccessToken()
    {
        var email = UniqueEmail();
        await fixture.Client.PostAsJsonAsync("/auth/signup", new { email, password = "CorrectPassw0rd!" });

        var response = await fixture.Client.PostAsJsonAsync("/auth/signin", new { email, password = "CorrectPassw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Signout_WithoutAccessToken_ReturnsUnauthorized()
    {
        var response = await fixture.Client.PostAsync("/auth/signout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signout_WithExpiredAccessToken_ReturnsUnauthorized()
    {
        var expiredToken = CreateExpiredAccessToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/signout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signout_WithValidAccessToken_RevokesSessionImmediately()
    {
        var (accessToken, refreshToken) = await SignupAndExtractTokens(UniqueEmail(), "SignoutPassw0rd!");

        using var signoutRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/signout");
        signoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var signoutResponse = await fixture.Client.SendAsync(signoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, signoutResponse.StatusCode);

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"refresh_token={refreshToken}");
        var refreshResponse = await fixture.Client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        var response = await fixture.Client.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesRefreshToken_AndRejectsReuseOfOldToken()
    {
        var (_, refreshToken) = await SignupAndExtractTokens(UniqueEmail(), "RefreshPassw0rd!");

        using var firstRefreshRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        firstRefreshRequest.Headers.Add("Cookie", $"refresh_token={refreshToken}");
        var firstRefreshResponse = await fixture.Client.SendAsync(firstRefreshRequest);
        Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode);
        var rotatedRefreshToken = ExtractRefreshToken(firstRefreshResponse);
        Assert.NotNull(rotatedRefreshToken);
        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        using var reuseRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        reuseRequest.Headers.Add("Cookie", $"refresh_token={refreshToken}");
        var reuseResponse = await fixture.Client.SendAsync(reuseRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        using var rotatedRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        rotatedRequest.Headers.Add("Cookie", $"refresh_token={rotatedRefreshToken}");
        var rotatedResponse = await fixture.Client.SendAsync(rotatedRequest);
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
    }

    private async Task<(string AccessToken, string RefreshToken)> SignupAndExtractTokens(string email, string password)
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/signup", new { email, password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = body.GetProperty("accessToken").GetString()!;
        var refreshToken = ExtractRefreshToken(response)!;

        return (accessToken, refreshToken);
    }

    private static string? ExtractRefreshToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith("refresh_token=", StringComparison.Ordinal))
            {
                return cookie.Split(';')[0]["refresh_token=".Length..];
            }
        }

        return null;
    }

    private static string CreateExpiredAccessToken()
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthEndpointsTestFixture.JwtSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("sid", "expired-session"),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@vsngrp-test.dev";
}
