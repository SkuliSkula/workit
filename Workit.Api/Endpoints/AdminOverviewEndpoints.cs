using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// The platform administrator's cross-company usage overview. Admin only —
/// owners never see another company's numbers. One request, a handful of
/// grouped queries; nothing here is per-row.
/// </summary>
internal static class AdminOverviewEndpoints
{
    /// <summary>How far back "recent" reaches. Long enough to see a monthly rhythm, short enough to show momentum.</summary>
    internal const int RecentDays = 30;

    internal static void MapAdminOverviewEndpoints(this WebApplication app)
    {
        var logger = app.Logger;

        app.MapGet("/api/auth/admin/overview", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    return Results.Ok(await BuildAsync(db, DateTimeOffset.UtcNow, ct));
                },
                logger,
                "building the admin overview"))
            .RequireAuthorization()
            .WithTags("Admin")
            .WithName("GetAdminOverview");
    }

    internal static async Task<AdminOverview> BuildAsync(WorkitDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff     = now.AddDays(-RecentDays);
        var cutoffDate = DateOnly.FromDateTime(cutoff.UtcDateTime);
        var cutoffUtc  = cutoff.UtcDateTime;

        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

        // ── Owners and their companies ────────────────────────────────────────
        var owners = await db.AppUsers.AsNoTracking()
            .Where(u => u.Role == WorkitRoles.Owner)
            .Select(u => new { u.Id, u.Email, u.CompanyId })
            .ToListAsync(ct);
        var ownerLinks = await db.UserCompanies.AsNoTracking()
            .Select(l => new { l.UserId, l.CompanyId, l.CreatedUtc })
            .ToListAsync(ct);
        var ownerEmailById = owners.ToDictionary(o => o.Id, o => o.Email);
        var ownerEmailsByCompany = ownerLinks
            .Where(l => ownerEmailById.ContainsKey(l.UserId))
            .GroupBy(l => l.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(l => ownerEmailById[l.UserId]).Distinct().OrderBy(e => e).ToList());
        var sinceByCompany = ownerLinks
            .GroupBy(l => l.CompanyId)
            .ToDictionary(g => g.Key, g => new DateTimeOffset(g.Min(l => l.CreatedUtc), TimeSpan.Zero));

        // ── Per-company aggregates ────────────────────────────────────────────
        var employees = await db.Employees.AsNoTracking()
            .GroupBy(e => e.CompanyId)
            .Select(g => new { CompanyId = g.Key, Total = g.Count(), Active = g.Count(e => e.IsActive) })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        var customers = await db.Customers.AsNoTracking()
            .GroupBy(c => c.CompanyId)
            .Select(g => new { CompanyId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Total, ct);

        var jobs = await db.Jobs.AsNoTracking()
            .GroupBy(j => j.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Total     = g.Count(),
                Recent    = g.Count(j => j.CreatedAt != null && j.CreatedAt >= cutoff),
                Last      = g.Max(j => j.CreatedAt),
            })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        var entries = await db.TimeEntries.AsNoTracking()
            .GroupBy(t => t.CompanyId)
            .Select(g => new
            {
                CompanyId   = g.Key,
                Total       = g.Count(),
                Recent      = g.Count(t => t.WorkDate >= cutoffDate),
                Hours       = g.Sum(t => t.Hours + t.OvertimeHours),
                HoursRecent = g.Where(t => t.WorkDate >= cutoffDate).Sum(t => t.Hours + t.OvertimeHours),
                LastDate    = g.Max(t => (DateOnly?)t.WorkDate),
            })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        var usages = await db.MaterialUsages.AsNoTracking()
            .GroupBy(u => u.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Total     = g.Count(),
                Recent    = g.Count(u => u.UsedAt >= cutoff),
                Last      = g.Max(u => (DateTimeOffset?)u.UsedAt),
            })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        var attachments = await db.JobAttachments.AsNoTracking()
            .GroupBy(a => a.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Total     = g.Count(),
                Recent    = g.Count(a => a.UploadedAt >= cutoff),
                Last      = g.Max(a => (DateTimeOffset?)a.UploadedAt),
            })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        var absences = await db.AbsenceRequests.AsNoTracking()
            .GroupBy(a => a.CompanyId)
            .Select(g => new { CompanyId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Total, ct);

        // Sign-ins: a refresh token is issued per login. Attributed to the
        // user's current company, which is the only one an employee has and
        // the one an owner last switched to.
        var logins = await db.RefreshTokens.AsNoTracking()
            .Join(db.AppUsers.AsNoTracking().Where(u => u.CompanyId != null),
                  t => t.UserId, u => u.Id,
                  (t, u) => new { CompanyId = u.CompanyId!.Value, t.CreatedUtc })
            .GroupBy(x => x.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Recent    = g.Count(x => x.CreatedUtc >= cutoffUtc),
                Last      = g.Max(x => (DateTime?)x.CreatedUtc),
            })
            .ToDictionaryAsync(x => x.CompanyId, ct);

        // ── Assemble ──────────────────────────────────────────────────────────
        var rows = companies.Select(c =>
        {
            var emp = employees.GetValueOrDefault(c.Id);
            var job = jobs.GetValueOrDefault(c.Id);
            var ent = entries.GetValueOrDefault(c.Id);
            var use = usages.GetValueOrDefault(c.Id);
            var att = attachments.GetValueOrDefault(c.Id);
            var log = logins.GetValueOrDefault(c.Id);

            var lastActivity = new[]
            {
                ent?.LastDate is DateOnly d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null,
                use?.Last,
                att?.Last,
                job?.Last,
            }.Max();

            return new AdminCompanyUsage
            {
                Id              = c.Id,
                Name            = c.Name,
                OwnerName       = c.Owner,
                OwnerEmails     = ownerEmailsByCompany.GetValueOrDefault(c.Id) ?? [],
                IsDemo          = string.Equals(c.Name, DemoDataSeeder.CompanyName, StringComparison.Ordinal),
                HasPayday       = !string.IsNullOrWhiteSpace(c.PaydayClientId),
                Since           = sinceByCompany.GetValueOrDefault(c.Id),
                Employees       = emp?.Total ?? 0,
                EmployeesActive = emp?.Active ?? 0,
                Customers       = customers.GetValueOrDefault(c.Id),
                Jobs            = job?.Total ?? 0,
                JobsRecent      = job?.Recent ?? 0,
                TimeEntries     = ent?.Total ?? 0,
                TimeEntriesRecent = ent?.Recent ?? 0,
                Hours           = ent?.Hours ?? 0,
                HoursRecent     = ent?.HoursRecent ?? 0,
                MaterialUsages  = use?.Total ?? 0,
                MaterialUsagesRecent = use?.Recent ?? 0,
                Attachments     = att?.Total ?? 0,
                AbsenceRequests = absences.GetValueOrDefault(c.Id),
                LoginsRecent    = log?.Recent ?? 0,
                LastActivity    = lastActivity,
                LastLogin       = log?.Last is DateTime l ? new DateTimeOffset(l, TimeSpan.Zero) : null,
            };
        }).ToList();

        var totals = new AdminPlatformTotals
        {
            Companies             = rows.Count,
            CompaniesActiveRecent = rows.Count(r => r.LastActivity >= cutoff),
            Owners                = owners.Count,
            OwnersWithoutCompany  = owners.Count(o => o.CompanyId == null || o.CompanyId == Guid.Empty),
            Employees             = rows.Sum(r => r.Employees),
            EmployeesActive       = rows.Sum(r => r.EmployeesActive),
            Customers             = rows.Sum(r => r.Customers),
            Jobs                  = rows.Sum(r => r.Jobs),
            JobsRecent            = rows.Sum(r => r.JobsRecent),
            TimeEntries           = rows.Sum(r => r.TimeEntries),
            TimeEntriesRecent     = rows.Sum(r => r.TimeEntriesRecent),
            Hours                 = rows.Sum(r => r.Hours),
            HoursRecent           = rows.Sum(r => r.HoursRecent),
            MaterialUsages        = rows.Sum(r => r.MaterialUsages),
            MaterialUsagesRecent  = rows.Sum(r => r.MaterialUsagesRecent),
            Attachments           = rows.Sum(r => r.Attachments),
            AttachmentsRecent     = attachments.Values.Sum(a => a.Recent),
            AbsenceRequests       = rows.Sum(r => r.AbsenceRequests),
            LoginsRecent          = rows.Sum(r => r.LoginsRecent),
        };

        return new AdminOverview
        {
            RecentDays  = RecentDays,
            GeneratedAt = now,
            Totals      = totals,
            Companies   = rows,
        };
    }
}
