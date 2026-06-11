namespace Workit.Shared.Models;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday customer id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
