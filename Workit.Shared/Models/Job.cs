namespace Workit.Shared.Models;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
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
}
