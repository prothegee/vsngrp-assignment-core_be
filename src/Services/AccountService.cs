using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VsngrpCoreBe.Data;
using VsngrpCoreBe.Models;

namespace VsngrpCoreBe.Services;

public enum SignupOutcome
{
    Created,
    DuplicateEmail,
}

public enum SigninOutcome
{
    Success,
    InvalidCredentials,
}

public interface IAccountService
{
    Task<(SignupOutcome Outcome, Account? Account)> SignupAsync(string email, string password);
    Task<(SigninOutcome Outcome, Account? Account)> SigninAsync(string email, string password);
}

public sealed class AccountService(IAppDbContextFactory dbContextFactory) : IAccountService
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly IPasswordHasher<Account> passwordHasher = new PasswordHasher<Account>();

    public async Task<(SignupOutcome Outcome, Account? Account)> SignupAsync(string email, string password)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        account.PasswordHash = passwordHasher.HashPassword(account, password);

        await using var writeContext = dbContextFactory.CreateWrite();
        writeContext.Accounts.Add(account);

        try
        {
            await writeContext.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return (SignupOutcome.DuplicateEmail, null);
        }

        return (SignupOutcome.Created, account);
    }

    public async Task<(SigninOutcome Outcome, Account? Account)> SigninAsync(string email, string password)
    {
        await using var readContext = dbContextFactory.CreateRead();
        var account = await readContext.Accounts.FirstOrDefaultAsync(candidate => candidate.Email == email);
        if (account is null)
        {
            return (SigninOutcome.InvalidCredentials, null);
        }

        var verifyResult = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return (SigninOutcome.InvalidCredentials, null);
        }

        return (SigninOutcome.Success, account);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresUniqueViolationSqlState;
}
