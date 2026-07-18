using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VsngrpCoreBe.Models;

namespace VsngrpCoreBe.Data;

public interface IAppDbContextFactory
{
    AppDbContext CreateWrite();
    AppDbContext CreateRead();
}

public sealed class AppDbContextFactory(PostgresConfig postgresConfig) : IAppDbContextFactory
{
    public AppDbContext CreateWrite() => Create(postgresConfig.Write.ConnectionString);

    public AppDbContext CreateRead() => Create(postgresConfig.Read.ConnectionString);

    private static AppDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}

public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MIGRATIONS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=vsngrp_core_be;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
