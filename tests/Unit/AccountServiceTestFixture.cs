using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using VsngrpCoreBe.Data;

namespace VsngrpCoreBe.Tests.Unit;

public sealed class AccountServiceTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18").Build();

    public IAppDbContextFactory DbContextFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        var connectionString = container.GetConnectionString();
        DbContextFactory = new SingleConnectionDbContextFactory(connectionString);

        await using var context = DbContextFactory.CreateWrite();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }

    private sealed class SingleConnectionDbContextFactory(string connectionString) : IAppDbContextFactory
    {
        public AppDbContext CreateWrite() => Create();

        public AppDbContext CreateRead() => Create();

        private AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;

            return new AppDbContext(options);
        }
    }
}
