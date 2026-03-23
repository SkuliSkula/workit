using FluentAssertions;
using Workit.Api.Auth;

namespace Workit.Tests.Auth;

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_ReturnsNonEmptyString()
    {
        var hash = PasswordHasher.HashPassword("MySecret123");
        hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void HashPassword_Format_HasThreeParts()
    {
        var hash = PasswordHasher.HashPassword("password");
        var parts = hash.Split('.', 3);
        parts.Should().HaveCount(3);
    }

    [Fact]
    public void HashPassword_FirstPart_IsIterationCount()
    {
        var hash = PasswordHasher.HashPassword("password");
        var parts = hash.Split('.', 3);
        int.TryParse(parts[0], out var iterations).Should().BeTrue();
        iterations.Should().Be(100_000);
    }

    [Fact]
    public void HashPassword_SecondPart_IsValidBase64Salt()
    {
        var hash = PasswordHasher.HashPassword("password");
        var parts = hash.Split('.', 3);
        var salt = Convert.FromBase64String(parts[1]);
        salt.Should().HaveCount(16); // SaltSize = 16
    }

    [Fact]
    public void HashPassword_ThirdPart_IsValidBase64Hash()
    {
        var hash = PasswordHasher.HashPassword("password");
        var parts = hash.Split('.', 3);
        var key = Convert.FromBase64String(parts[2]);
        key.Should().HaveCount(32); // KeySize = 32
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashes_ForSamePassword()
    {
        var hash1 = PasswordHasher.HashPassword("password");
        var hash2 = PasswordHasher.HashPassword("password");
        hash1.Should().NotBe(hash2, "because random salt should differ");
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.HashPassword("MySecret123");
        PasswordHasher.VerifyPassword("MySecret123", hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.HashPassword("MySecret123");
        PasswordHasher.VerifyPassword("WrongPassword", hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_CaseSensitive()
    {
        var hash = PasswordHasher.HashPassword("Password");
        PasswordHasher.VerifyPassword("password", hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_CanBeHashedAndVerified()
    {
        var hash = PasswordHasher.HashPassword("");
        PasswordHasher.VerifyPassword("", hash).Should().BeTrue();
        PasswordHasher.VerifyPassword("notempty", hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_MalformedHash_NoParts_ReturnsFalse()
    {
        PasswordHasher.VerifyPassword("password", "not-a-valid-hash").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_MalformedHash_NonIntegerIterations_ReturnsFalse()
    {
        PasswordHasher.VerifyPassword("password", "abc.AAAA.BBBB").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_MalformedHash_InvalidBase64_ReturnsFalse()
    {
        PasswordHasher.VerifyPassword("password", "100000.!!!invalid!!!.!!!invalid!!!").Should().BeFalse();
    }
}
