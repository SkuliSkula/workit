namespace Workit.Shared.Models;

public sealed class ToolAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ToolId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReturnedAt { get; set; }

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;
}
