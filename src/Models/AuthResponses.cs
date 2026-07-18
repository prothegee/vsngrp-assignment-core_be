namespace VsngrpCoreBe.Models;

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
}

public sealed class HealthResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = string.Empty;
    public string GitSha { get; set; } = string.Empty;
}
