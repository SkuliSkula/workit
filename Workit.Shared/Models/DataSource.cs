namespace Workit.Shared.Models;

/// <summary>Where a record originated — manually created in Workit, or synced from an external system.</summary>
public enum DataSource
{
    Workit = 0,
    Payday = 1
}
