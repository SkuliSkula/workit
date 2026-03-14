namespace Workit.Shared.Payday;

public sealed class PaydayEmployee
{
    public Guid Id { get; set; }
    public string? Ssn { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; }
}
