using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using VsngrpCoreBe.Data;
using VsngrpCoreBe.Models;
using VsngrpCoreBe.Services;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["CONFIG_PATH"] ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "config", "config.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);

var appConfig = builder.Configuration.Get<AppConfig>() ?? throw new InvalidOperationException("config.json failed to bind to AppConfig.");
builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(appConfig.Postgres);

builder.WebHost.UseUrls($"http://0.0.0.0:{appConfig.Port}");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(appConfig.Redis.ConnectionString));
builder.Services.AddSingleton<IAppDbContextFactory, AppDbContextFactory>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthorizationHandler, SessionAuthorizationHandler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appConfig.JwtSecret)),
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveSession", policy => policy.Requirements.Add(new ActiveSessionRequirement()));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(appConfig.CorsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new HealthResponse
{
    Status = "ok",
    Version = appConfig.Version,
    GitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "dev",
}));

app.Run();

public partial class Program;
