namespace VsngrpCoreBe.Models;

public sealed class AppConfig
{
    public int Port { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string JwtSecret { get; set; } = string.Empty;
    public PostgresConfig Postgres { get; set; } = new();
    public RedisConfig Redis { get; set; } = new();
    public string[] CorsAllowedOrigins { get; set; } = [];
}

public sealed class PostgresConfig
{
    public ConnectionStringConfig Write { get; set; } = new();
    public ConnectionStringConfig Read { get; set; } = new();
}

public sealed class RedisConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class ConnectionStringConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}
