using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace VsngrpCoreBe.Services;

public interface ISessionService
{
    Task<string> CreateSessionAsync(Guid accountId, TimeSpan ttl);
    Task<Guid?> GetAccountIdAsync(string sessionId);
    Task ExtendSessionAsync(string sessionId, Guid accountId, TimeSpan ttl);
    Task DeleteSessionAsync(string sessionId);

    Task<string> IssueRefreshTokenAsync(string sessionId, TimeSpan ttl);
    Task<string?> ConsumeRefreshTokenAsync(string refreshToken);
}

public sealed class SessionService(IConnectionMultiplexer redis) : ISessionService
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private const string SessionKeyPrefix = "session:";
    private const string RefreshKeyPrefix = "refresh:";

    private IDatabase Database => redis.GetDatabase();

    public async Task<string> CreateSessionAsync(Guid accountId, TimeSpan ttl)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await Database.StringSetAsync(SessionKeyPrefix + sessionId, accountId.ToString(), ttl);

        return sessionId;
    }

    public async Task<Guid?> GetAccountIdAsync(string sessionId)
    {
        var value = await Database.StringGetAsync(SessionKeyPrefix + sessionId);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return Guid.Parse(value.ToString());
    }

    public async Task ExtendSessionAsync(string sessionId, Guid accountId, TimeSpan ttl)
    {
        await Database.StringSetAsync(SessionKeyPrefix + sessionId, accountId.ToString(), ttl);
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await Database.KeyDeleteAsync(SessionKeyPrefix + sessionId);
    }

    public async Task<string> IssueRefreshTokenAsync(string sessionId, TimeSpan ttl)
    {
        var refreshToken = GenerateOpaqueToken();
        await Database.StringSetAsync(RefreshKeyPrefix + Hash(refreshToken), sessionId, ttl);

        return refreshToken;
    }

    public async Task<string?> ConsumeRefreshTokenAsync(string refreshToken)
    {
        var key = RefreshKeyPrefix + Hash(refreshToken);
        var sessionId = await Database.StringGetAsync(key);
        if (sessionId.IsNullOrEmpty)
        {
            return null;
        }

        await Database.KeyDeleteAsync(key);

        return sessionId.ToString();
    }

    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(bytes);
    }
}
