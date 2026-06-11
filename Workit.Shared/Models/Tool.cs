namespace Workit.Shared.Models;

public sealed class Tool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;
}
