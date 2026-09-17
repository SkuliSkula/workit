namespace Workit.Shared.Api;

/// <summary>
/// The platform administrator's view of every company on Workit: lifetime
/// totals, what happened in the recent window, and per-company usage with a
/// last-activity date. Returned by <c>GET /api/auth/admin/overview</c>.
/// </summary>
public sealed class AdminOverview
{
    /// <summary>Length of the "recent" window the *Recent counts cover, in days.</summary>
    public int RecentDays { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public AdminPlatformTotals Totals { get; set; } = new();
    public List<AdminCompanyUsage> Companies { get; set; } = [];
}

public sealed class AdminPlatformTotals
{
    public int Companies { get; set; }
    /// <summary>Companies with any activity inside the recent window.</summary>
    public int CompaniesActiveRecent { get; set; }
    public int Owners { get; set; }
    /// <summary>Owner accounts that have not set up a company yet.</summary>
    public int OwnersWithoutCompany { get; set; }
    public int Employees { get; set; }
    public int EmployeesActive { get; set; }
    public int Customers { get; set; }
    public int Jobs { get; set; }
    public int JobsRecent { get; set; }
    public int TimeEntries { get; set; }
    public int TimeEntriesRecent { get; set; }
    public decimal Hours { get; set; }
    public decimal HoursRecent { get; set; }
    public int MaterialUsages { get; set; }
    public int MaterialUsagesRecent { get; set; }
    public int Attachments { get; set; }
    public int AttachmentsRecent { get; set; }
    public int AbsenceRequests { get; set; }
    /// <summary>Sign-ins (refresh tokens issued) inside the recent window, all roles.</summary>
    public int LoginsRecent { get; set; }
}

public sealed class AdminCompanyUsage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public List<string> OwnerEmails { get; set; } = [];
    /// <summary>The seeded demo company — real-looking numbers that are not a customer.</summary>
    public bool IsDemo { get; set; }
    public bool HasPayday { get; set; }
    /// <summary>When the first owner was linked to the company; the closest thing to a created date.</summary>
    public DateTimeOffset? Since { get; set; }

    public int Employees { get; set; }
    public int EmployeesActive { get; set; }
    public int Customers { get; set; }
    public int Jobs { get; set; }
    public int JobsRecent { get; set; }
    public int TimeEntries { get; set; }
    public int TimeEntriesRecent { get; set; }
    public decimal Hours { get; set; }
    public decimal HoursRecent { get; set; }
    public int MaterialUsages { get; set; }
    public int MaterialUsagesRecent { get; set; }
    public int Attachments { get; set; }
    public int AbsenceRequests { get; set; }
    public int LoginsRecent { get; set; }

    /// <summary>Most recent of: time entry work date, material usage, file upload, job created. Null when nothing was ever logged.</summary>
    public DateTimeOffset? LastActivity { get; set; }
    /// <summary>Most recent sign-in by any of the company's users, if any.</summary>
    public DateTimeOffset? LastLogin { get; set; }
}
