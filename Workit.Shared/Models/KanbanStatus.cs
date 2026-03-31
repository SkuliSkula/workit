namespace Workit.Shared.Models;

public enum KanbanStatus
{
    Active  = 0,   // lane is derived from time-entry state
    Waiting = 1    // manually placed on hold by the owner
}
