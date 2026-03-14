namespace Workit.Shared.Payday;

public sealed class PaydayCompany
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? Ssn { get; set; }
    public string? Phone { get; set; }
    public string? VatNumber { get; set; }
    public DateTime Created { get; set; }
    public bool HasClaimCollection { get; set; }
}
