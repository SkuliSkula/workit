using FluentAssertions;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// A job's status — the Kanban lane — is sent on every job and filtered on in
/// the console and both apps, so the rule has to be the same everywhere.
/// </summary>
public class JobLaneTests
{
    private static readonly Job Active  = new() { KanbanStatus = KanbanStatus.Active };
    private static readonly Job OnHold  = new() { KanbanStatus = KanbanStatus.Waiting };

    [Fact]
    public void No_hours_is_backlog_even_when_on_hold() =>
        JobLanes.Of(OnHold, entries: 0, uninvoiced: 0).Should().Be(JobLane.Backlog);

    [Fact]
    public void Every_hour_invoiced_is_done_even_when_on_hold() =>
        JobLanes.Of(OnHold, entries: 5, uninvoiced: 0).Should().Be(JobLane.Done);

    [Fact]
    public void Open_hours_are_in_progress_unless_the_owner_put_it_on_hold()
    {
        JobLanes.Of(Active, entries: 5, uninvoiced: 2).Should().Be(JobLane.InProgress);
        JobLanes.Of(OnHold, entries: 5, uninvoiced: 2).Should().Be(JobLane.Waiting);
    }

    [Fact]
    public void The_entry_list_form_agrees_with_the_counts()
    {
        var entries = new List<TimeEntry> { new() { IsInvoiced = true }, new() { IsInvoiced = false } };
        JobLanes.Of(Active, entries).Should().Be(JobLanes.Of(Active, 2, 1));
    }

    [Fact]
    public void The_numbers_the_apps_decode_never_change()
    {
        ((int)JobLane.Backlog).Should().Be(0);
        ((int)JobLane.InProgress).Should().Be(1);
        ((int)JobLane.Waiting).Should().Be(2);
        ((int)JobLane.Done).Should().Be(3);
    }
}
