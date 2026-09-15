using FluentAssertions;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Endpoints;

/// <summary>
/// Every Company the API returns goes through WithoutSecrets(): the stored Payday
/// credentials must never appear in a response, only a boolean flag.
/// </summary>
public class CompanySecretsTests
{
    private static Company StoredCompany() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Rafvirki ehf.",
        Ssn = "5501012340",
        Email = "r@test.is",
        Address = "Ármúli 1",
        Phone = "5551000",
        Owner = "Jón",
        DrivingUnitPrice = 120,
        StandardHoursPerDay = 7.5m,
        ZipCode = "108",
        City = "Reykjavík",
        VatNumber = "12345",
        Source = DataSource.Payday,
        PaydayId = Guid.NewGuid(),
        PaydayClientId = "CfDJ8-encrypted-client-id",
        PaydayClientSecret = "CfDJ8-encrypted-client-secret",
    };

    [Fact]
    public void WithoutSecrets_StripsCredentials_AndSetsFlag()
    {
        var result = StoredCompany().WithoutSecrets();

        result.PaydayClientId.Should().BeNull();
        result.PaydayClientSecret.Should().BeNull();
        result.HasPaydayCredentials.Should().BeTrue();
    }

    [Fact]
    public void WithoutSecrets_FlagIsFalse_WhenEitherCredentialMissing()
    {
        var onlyId = StoredCompany();
        onlyId.PaydayClientSecret = null;
        onlyId.WithoutSecrets().HasPaydayCredentials.Should().BeFalse();

        var none = StoredCompany();
        none.PaydayClientId = null;
        none.PaydayClientSecret = null;
        none.WithoutSecrets().HasPaydayCredentials.Should().BeFalse();
    }

    [Fact]
    public void WithoutSecrets_DoesNotMutateTheStoredEntity()
    {
        var stored = StoredCompany();

        var result = stored.WithoutSecrets();

        result.Should().NotBeSameAs(stored);
        stored.PaydayClientId.Should().Be("CfDJ8-encrypted-client-id");
        stored.PaydayClientSecret.Should().Be("CfDJ8-encrypted-client-secret");
    }

    [Fact]
    public void WithoutSecrets_CopiesEveryNonSecretField()
    {
        var stored = StoredCompany();

        var result = stored.WithoutSecrets();

        result.Should().BeEquivalentTo(stored, opts => opts
            .Excluding(c => c.PaydayClientId)
            .Excluding(c => c.PaydayClientSecret)
            .Excluding(c => c.HasPaydayCredentials));
    }
}
