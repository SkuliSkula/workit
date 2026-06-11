namespace Workit.Shared.Models;

public sealed class PaydayExpenseLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid PaydayExpenseId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>JSON snapshot of the Payday expense at link time, so JobDetails works without Payday credentials.</summary>
    public string? SnapshotJson { get; set; }
}
