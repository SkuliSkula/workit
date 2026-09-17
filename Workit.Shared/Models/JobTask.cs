namespace Workit.Shared.Models;

public enum JobTaskStatus
{
    Open = 0,
    Done = 1,
}

/// <summary>
/// A short piece of work inside a job — a day or two, a handful of hours —
/// for customers whose jobs run long. Optional: a job with no tasks behaves
/// exactly as before. Named JobTask because <c>Task</c> is taken by the
/// runtime; the UI just says "task".
/// </summary>
public sealed class JobTask : ICreatedAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid JobId { get; set; }

    /// <summary>Running number within the job, 1..n; assigned by the API.</summary>
    public int TaskNumber { get; set; }
    /// <summary>The job's code and the task number: <c>MNT102-3</c>. Assigned by the API.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JobTaskStatus Status { get; set; } = JobTaskStatus.Open;

    /// <summary>A guideline, not a cap — the apps warn when logged hours pass it.</summary>
    public decimal? EstimatedHours { get; set; }

    /// <summary>
    /// Who is on this task. Empty means everyone assigned to the job. Stored as
    /// a Postgres uuid[] like <see cref="Job.AssignedEmployeeIds"/>.
    /// </summary>
    public List<Guid> AssignedEmployeeIds { get; set; } = [];

    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneByUserId { get; set; }

    /// <summary>Owner-chosen order within the job; ties fall back to TaskNumber.</summary>
    public int SortOrder { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <inheritdoc/>
    public Guid? CreatedByUserId { get; set; }
    /// <inheritdoc/>
    public string? CreatedByName { get; set; }
}
