namespace Workit.Shared.Models;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>
    /// The employees put on this job. Empty for most jobs — they are shared —
    /// and a job can exist before anyone is assigned. Stored as a Postgres
    /// uuid[]; order is not significant and ids are unique.
    /// </summary>
    public List<Guid> AssignedEmployeeIds { get; set; } = [];

    public string       Name          { get; set; } = string.Empty;
    public string       Code          { get; set; } = string.Empty;
    public BillingType  BillingType   { get; set; } = BillingType.Hourly;
    public JobCategory  Category      { get; set; } = JobCategory.NewInstallation;
    public int          JobNumber     { get; set; }
    public KanbanStatus      KanbanStatus      { get; set; } = KanbanStatus.Active;
    public string?           WaitingReason     { get; set; }
    public DateTimeOffset?   KanbanInProgressAt { get; set; }
    public DateTimeOffset?   KanbanWaitingAt    { get; set; }
    public DateTimeOffset?   KanbanDoneAt       { get; set; }

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;
}
