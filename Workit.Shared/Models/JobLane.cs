namespace Workit.Shared.Models;

/// <summary>
/// Where a job stands — the Kanban board's lanes, the Jobs pages' status filter
/// in the console and the apps. The API sends it on every job (<see cref="Job.Lane"/>).
/// </summary>
public enum JobLane { Backlog = 0, InProgress = 1, Waiting = 2, Done = 3 }

public static class JobLanes
{
    public static readonly JobLane[] All = [JobLane.Backlog, JobLane.InProgress, JobLane.Waiting, JobLane.Done];

    /// <summary>
    /// No hours yet: backlog. Every hour invoiced: done. Otherwise in progress,
    /// unless the owner put it on hold (waiting).
    /// </summary>
    public static JobLane Of(Job job, IReadOnlyCollection<TimeEntry> entriesOnJob) =>
        Of(job, entriesOnJob.Count, entriesOnJob.Count(e => !e.IsInvoiced));

    /// <param name="entries">Time entries on the job, everyone's.</param>
    /// <param name="uninvoiced">How many of them are not invoiced yet.</param>
    public static JobLane Of(Job job, int entries, int uninvoiced)
    {
        if (entries == 0)                             return JobLane.Backlog;
        if (uninvoiced == 0)                          return JobLane.Done;
        if (job.KanbanStatus == KanbanStatus.Waiting) return JobLane.Waiting;
        return JobLane.InProgress;
    }

    public static string Label(JobLane lane) => lane switch
    {
        JobLane.Backlog    => "Backlog",
        JobLane.InProgress => "In progress",
        JobLane.Waiting    => "Waiting",
        _                  => "Done",
    };

    /// <summary>The short name used in the page address (<c>?status=backlog,waiting</c>).</summary>
    public static string Key(JobLane lane) => lane switch
    {
        JobLane.Backlog    => "backlog",
        JobLane.InProgress => "inprogress",
        JobLane.Waiting    => "waiting",
        _                  => "done",
    };
}
