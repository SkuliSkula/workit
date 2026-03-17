namespace Workit.Shared.Models;

public sealed class MaterialUsage
{
    public Guid   Id         { get; set; } = Guid.NewGuid();
    public Guid   CompanyId  { get; set; }
    public Guid   MaterialId { get; set; }
    public Guid   EmployeeId { get; set; }

    /// <summary>Optional link to a job</summary>
    public Guid?  JobId      { get; set; }

    public decimal Quantity  { get; set; }

    public DateTimeOffset UsedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Notes      { get; set; } = string.Empty;
}
