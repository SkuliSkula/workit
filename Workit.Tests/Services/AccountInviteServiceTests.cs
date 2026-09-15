using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Services;

namespace Workit.Tests.Services;

/// <summary>
/// New accounts are invited rather than sent a password. These pin the parts a
/// regression would quietly break: that the link is the only way in, that it is
/// long-lived enough for a real person to act on, and that an older link stops
/// working once a newer one is issued.
/// </summary>
public class AccountInviteServiceTests
{
    private static (AccountInviteService service, WorkitDbContext db, FakeEmailService email) Create(
        string? appUrl = "https://help.workit.is")
    {
        var options = new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"invites-{Guid.NewGuid()}")
            .Options;
        var db = new WorkitDbContext(options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:Url"] = appUrl })
            .Build();

        var email = new FakeEmailService();
        var service = new AccountInviteService(db, email, configuration,
            NullLogger<AccountInviteService>.Instance);

        return (service, db, email);
    }

    [Fact]
    public async Task SendInviteAsync_IssuesASingleUnusedToken()
    {
        var (service, db, _) = Create();

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);

        var tokens = await db.PasswordResetTokens.ToListAsync();
        tokens.Should().ContainSingle();
        tokens[0].Used.Should().BeFalse();
        tokens[0].Email.Should().Be("owner@example.test");
    }

    /// <summary>
    /// A new hire may not open their email for days. An hour — the password-reset
    /// lifetime — would turn most invites into a support call.
    /// </summary>
    [Fact]
    public async Task SendInviteAsync_TokenLastsSevenDays()
    {
        var (service, db, _) = Create();

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);

        var token = await db.PasswordResetTokens.SingleAsync();
        (token.ExpiresAt - token.CreatedAt).TotalDays.Should().BeApproximately(7, 0.01);
    }

    [Fact]
    public async Task SendInviteAsync_SupersedesAnEarlierInvite()
    {
        var (service, db, _) = Create();

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);
        var first = await db.PasswordResetTokens.SingleAsync();

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);

        var tokens = await db.PasswordResetTokens.OrderBy(t => t.CreatedAt).ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Single(t => t.Token == first.Token).Used.Should().BeTrue();
        tokens.Count(t => !t.Used).Should().Be(1);
    }

    [Fact]
    public async Task SendInviteAsync_LinkCarriesTheTokenAndTheNewAccountFlag()
    {
        var (service, db, email) = Create();

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);

        var token = await db.PasswordResetTokens.SingleAsync();
        email.OwnerInvites.Should().ContainSingle();
        email.OwnerInvites[0].SetupUrl.Should()
            .Be($"https://help.workit.is/reset-password?token={token.Token}&new=1");
    }

    [Fact]
    public async Task SendInviteAsync_EmployeeKind_UsesTheEmployeeEmail()
    {
        var (service, _, email) = Create();

        await service.SendInviteAsync("worker@example.test", "Worker", InviteKind.Employee);

        email.EmployeeInvites.Should().ContainSingle();
        email.OwnerInvites.Should().BeEmpty();
        email.EmployeeInvites[0].Name.Should().Be("Worker");
    }

    [Fact]
    public async Task SendInviteAsync_NormalisesTheAddress()
    {
        var (service, db, email) = Create();

        await service.SendInviteAsync("  Owner@Example.TEST  ", "Owner", InviteKind.Owner);

        (await db.PasswordResetTokens.SingleAsync()).Email.Should().Be("owner@example.test");
        email.OwnerInvites[0].Email.Should().Be("owner@example.test");
    }

    /// <summary>
    /// The demo logins are filed with Apple App Review. Inviting one would let a
    /// redeemed link change the password the reviewers have on file.
    /// </summary>
    [Theory]
    [InlineData("demo@workit.is")]
    [InlineData("demo.employee@workit.is")]
    public async Task SendInviteAsync_ProtectedDemoAccount_IssuesNothing(string email)
    {
        var (service, db, fakeEmail) = Create();

        await service.SendInviteAsync(email, "Demo", InviteKind.Owner);

        (await db.PasswordResetTokens.CountAsync()).Should().Be(0);
        fakeEmail.OwnerInvites.Should().BeEmpty();
        fakeEmail.EmployeeInvites.Should().BeEmpty();
    }

    [Fact]
    public async Task SendInviteAsync_MissingAppUrl_FallsBackToTheMarketingSite()
    {
        var (service, _, email) = Create(appUrl: null);

        await service.SendInviteAsync("owner@example.test", "Owner", InviteKind.Owner);

        email.OwnerInvites[0].SetupUrl.Should().StartWith("https://help.workit.is/reset-password?token=");
    }

    [Fact]
    public void CreateUnusablePassword_IsDifferentEveryTime()
    {
        var (service, _, _) = Create();

        var passwords = Enumerable.Range(0, 50).Select(_ => service.CreateUnusablePassword()).ToList();

        passwords.Should().OnlyHaveUniqueItems();
        passwords.Should().OnlyContain(p => p.Length >= 32);
    }

    private sealed class FakeEmailService : IEmailService
    {
        public List<(string Name, string Email, string SetupUrl)> OwnerInvites { get; } = [];
        public List<(string Name, string Email, string SetupUrl)> EmployeeInvites { get; } = [];

        public Task SendOwnerInviteAsync(string name, string email, string setupUrl)
        {
            OwnerInvites.Add((name, email, setupUrl));
            return Task.CompletedTask;
        }

        public Task SendEmployeeInviteAsync(string name, string email, string setupUrl)
        {
            EmployeeInvites.Add((name, email, setupUrl));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string email, string resetUrl) => Task.CompletedTask;

        public Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType,
            DateOnly start, DateOnly end) => Task.CompletedTask;

        public Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType,
            DateOnly start, DateOnly end, bool approved, string reviewNotes) => Task.CompletedTask;
    }
}
