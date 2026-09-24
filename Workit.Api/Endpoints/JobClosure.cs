using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;

namespace Workit.Api.Endpoints;

/// <summary>
/// A job is finished once everything logged on it has been billed: it has at
/// least one time entry, every entry is invoiced, and so is every material used
/// on it. Nothing stores that — it follows from the entries, the same way the
/// console's Kanban derives its Done lane — so a job cannot drift out of step
/// with its own hours.
///
/// A finished job is read-only for the crew: they cannot log, change or delete
/// anything on it, and the apps stop offering it. There is no reopen; hours
/// that belong to a new visit belong to a new job.
/// </summary>
internal static class JobClosure
{
    /// <summary>Every finished job in the company, in two grouped queries.</summary>
    internal static async Task<HashSet<Guid>> FinishedJobIdsAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var hours = await db.TimeEntries
            .Where(e => e.CompanyId == companyId)
            .GroupBy(e => e.JobId)
            .Select(g => new { JobId = g.Key, Open = g.Count(e => !e.IsInvoiced) })
            .ToListAsync(ct);

        var withOpenMaterials = (await db.MaterialUsages
            .Where(u => u.CompanyId == companyId && u.JobId != null && !u.IsInvoiced)
            .Select(u => u.JobId!.Value)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        return hours
            .Where(h => h.Open == 0 && !withOpenMaterials.Contains(h.JobId))
            .Select(h => h.JobId)
            .ToHashSet();
    }

    /// <summary>The same rule for one job.</summary>
    internal static async Task<bool> IsFinishedAsync(WorkitDbContext db, Guid companyId, Guid jobId, CancellationToken ct)
    {
        var entries = await db.TimeEntries
            .Where(e => e.CompanyId == companyId && e.JobId == jobId)
            .Select(e => e.IsInvoiced)
            .ToListAsync(ct);
        if (entries.Count == 0 || entries.Any(invoiced => !invoiced)) return false;

        return !await db.MaterialUsages
            .AnyAsync(u => u.CompanyId == companyId && u.JobId == jobId && !u.IsInvoiced, ct);
    }

    /// <summary>True when this caller must be kept out of a finished job.</summary>
    internal static async Task<bool> BlocksAsync(WorkitDbContext db, HttpContext http, Guid companyId, Guid jobId, CancellationToken ct) =>
        !http.User.IsOwnerOrAdmin() && await IsFinishedAsync(db, companyId, jobId, ct);

    internal const string FinishedMessage =
        "This job is finished — everything on it has been invoiced. Ask the office to open a new job for more work.";
}
