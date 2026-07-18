using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using VsngrpCoreBe.Data;

namespace VsngrpCoreBe.Tests.Integration;

public sealed class AuthEndpointsTestFixture : IAsyncLifetime
{
    public const string JwtSecret = "integration-test-jwt-secret-value-not-real";

    private readonly PostgreSqlContainer postgresContainer = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer redisContainer = new RedisBuilder("redis:8").Build();
    private string configPath = string.Empty;
    private CoreBeWebApplicationFactory factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgresContainer.StartAsync(), redisContainer.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgresContainer.GetConnectionString()).Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        configPath = Path.Combine(Path.GetTempPath(), $"vsngrp-core-be-test-config-{Guid.NewGuid():N}.json");
        var config = new
        {
            port = 0,
            version = "0.1.0-test",
            jwtSecret = JwtSecret,
            postgres = new
            {
                write = new { connectionString = postgresContainer.GetConnectionString() },
                read = new { connectionString = postgresContainer.GetConnectionString() },
            },
            redis = new { connectionString = redisContainer.GetConnectionString() },
            corsAllowedOrigins = new[] { "http://localhost:9003" },
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        factory = new CoreBeWebApplicationFactory(configPath);
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        File.Delete(configPath);
        await Task.WhenAll(postgresContainer.DisposeAsync().AsTask(), redisContainer.DisposeAsync().AsTask());
    }
}
