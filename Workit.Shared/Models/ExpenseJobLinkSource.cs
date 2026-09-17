namespace Workit.Shared.Models;

/// <summary>How an expense came to be linked — or deliberately not linked — to a job.</summary>
public enum ExpenseJobLinkSource
{
    /// <summary>No link has ever been made.</summary>
    None = 0,
    /// <summary>An owner picked the job.</summary>
    Manual = 1,
    /// <summary>Workit found the job code on the expense and linked it.</summary>
    Automatic = 2,
    /// <summary>An owner removed the link; automatic linking leaves this expense alone from then on.</summary>
    RemovedByOwner = 3,
}
