using FluentAssertions;
using Workit.Api.Data;

namespace Workit.Tests.Data;

/// <summary>
/// The demo credentials are filed with Apple App Review, so every code path that
/// could rewrite their password has to refuse. These tests pin the predicate
/// those paths share.
/// </summary>
public class DemoAccountProtectionTests
{
    [Theory]
    [InlineData("demo@workit.is")]
    [InlineData("demo.employee@workit.is")]
    public void IsProtectedAccount_DemoLogins_AreProtected(string email)
    {
        DemoDataSeeder.IsProtectedAccount(email).Should().BeTrue();
    }

    [Theory]
    [InlineData("DEMO@WORKIT.IS")]
    [InlineData("Demo.Employee@Workit.is")]
    public void IsProtectedAccount_IgnoresCasing(string email)
    {
        DemoDataSeeder.IsProtectedAccount(email).Should().BeTrue();
    }

    [Theory]
    [InlineData("  demo@workit.is  ")]
    [InlineData("\tdemo.employee@workit.is\n")]
    public void IsProtectedAccount_IgnoresSurroundingWhitespace(string email)
    {
        DemoDataSeeder.IsProtectedAccount(email).Should().BeTrue();
    }

    [Theory]
    [InlineData("jon@test.is")]
    [InlineData("demo@example.com")]
    [InlineData("notdemo@workit.is")]
    [InlineData("demo@workit.is.attacker.com")]
    public void IsProtectedAccount_OtherAddresses_AreNotProtected(string email)
    {
        DemoDataSeeder.IsProtectedAccount(email).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProtectedAccount_MissingEmail_IsNotProtected(string? email)
    {
        DemoDataSeeder.IsProtectedAccount(email).Should().BeFalse();
    }

    [Fact]
    public void SeededPassword_MatchesTheCredentialFiledWithAppReview()
    {
        // Changing this constant silently invalidates the credentials in
        // App Store Connect, so make that a deliberate, visible edit.
        DemoDataSeeder.Password.Should().Be("WorkitDemo2026!");
    }
}
