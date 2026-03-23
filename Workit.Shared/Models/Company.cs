namespace Workit.Shared.Models;

public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public decimal DrivingUnitPrice { get; set; }
    public decimal StandardHoursPerDay { get; set; } = 8m;

    // Payday integration credentials (per-company)
    public string? PaydayClientId { get; set; }
    public string? PaydayClientSecret { get; set; }
}
