namespace Workit.Shared.Payday;

public sealed class PaydayPensionFund
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string? Name { get; set; }
    public bool Authenticates { get; set; }
}
