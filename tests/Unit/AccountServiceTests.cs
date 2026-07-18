using VsngrpCoreBe.Services;

namespace VsngrpCoreBe.Tests.Unit;

public sealed class AccountServiceTests(AccountServiceTestFixture fixture) : IClassFixture<AccountServiceTestFixture>
{
    private AccountService CreateService() => new(fixture.DbContextFactory);

    [Fact]
    public async Task SignupAsync_NewEmail_CreatesAccountWithHashedPassword()
    {
        var service = CreateService();
        var email = UniqueEmail();

        var (outcome, account) = await service.SignupAsync(email, "SignupPassw0rd!");

        Assert.Equal(SignupOutcome.Created, outcome);
        Assert.NotNull(account);
        Assert.Equal(email, account.Email);
        Assert.NotEqual("SignupPassw0rd!", account.PasswordHash);
    }

    [Fact]
    public async Task SignupAsync_DuplicateEmail_ReturnsDuplicateOutcome()
    {
        var service = CreateService();
        var email = UniqueEmail();
        await service.SignupAsync(email, "FirstPassw0rd!");

        var (outcome, account) = await service.SignupAsync(email, "SecondPassw0rd!");

        Assert.Equal(SignupOutcome.DuplicateEmail, outcome);
        Assert.Null(account);
    }

    [Fact]
    public async Task SigninAsync_CorrectPassword_ReturnsSuccess()
    {
        var service = CreateService();
        var email = UniqueEmail();
        await service.SignupAsync(email, "CorrectPassw0rd!");

        var (outcome, account) = await service.SigninAsync(email, "CorrectPassw0rd!");

        Assert.Equal(SigninOutcome.Success, outcome);
        Assert.NotNull(account);
        Assert.Equal(email, account.Email);
    }

    [Fact]
    public async Task SigninAsync_WrongPassword_ReturnsInvalidCredentials()
    {
        var service = CreateService();
        var email = UniqueEmail();
        await service.SignupAsync(email, "CorrectPassw0rd!");

        var (outcome, account) = await service.SigninAsync(email, "WrongPassw0rd!");

        Assert.Equal(SigninOutcome.InvalidCredentials, outcome);
        Assert.Null(account);
    }

    [Fact]
    public async Task SigninAsync_UnknownEmail_ReturnsInvalidCredentials()
    {
        var service = CreateService();

        var (outcome, account) = await service.SigninAsync(UniqueEmail(), "AnyPassw0rd!");

        Assert.Equal(SigninOutcome.InvalidCredentials, outcome);
        Assert.Null(account);
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@vsngrp-test.dev";
}
