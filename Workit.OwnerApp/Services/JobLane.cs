using Workit.Shared.Models;

namespace Workit.OwnerApp.Services;

/// <summary>Where a job stands — the Kanban board's lanes, also the Jobs page's status filter.</summary>
public enum JobLane { Backlog, InProgress, Waiting, Done }

public static class JobLanes
{
    public static readonly JobLane[] All = [JobLane.Backlog, JobLane.InProgress, JobLane.Waiting, JobLane.Done];

    /// <summary>
    /// No hours yet: backlog. Every hour invoiced: done. Otherwise in progress,
    /// unless the owner put it on hold (waiting).
    /// </summary>
    public static JobLane Of(Job job, IReadOnlyCollection<TimeEntry> entriesOnJob)
    {
        if (entriesOnJob.Count == 0)                  return JobLane.Backlog;
        if (entriesOnJob.All(e => e.IsInvoiced))      return JobLane.Done;
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
