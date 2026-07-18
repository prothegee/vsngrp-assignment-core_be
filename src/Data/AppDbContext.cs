using Microsoft.EntityFrameworkCore;
using VsngrpCoreBe.Models;

namespace VsngrpCoreBe.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Email).IsRequired();
            entity.Property(account => account.PasswordHash).IsRequired();
            entity.HasIndex(account => account.Email).IsUnique();
        });
    }
}
