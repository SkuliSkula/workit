namespace Workit.Shared.Payday;

public sealed class PaydayOptions
{
    public const string SectionName = "Payday";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
