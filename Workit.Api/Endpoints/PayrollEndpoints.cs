using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using Workit.Shared.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// A month of Workit hours → Payday payroll. The preview is what the owner
/// checks; the export posts the same numbers as a timesheet and keeps a row
/// per send so a period can be corrected and sent again with a visible trail.
/// </summary>
internal static class PayrollEndpoints
{
    internal static void MapPayrollEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var api = app.MapGroup("/api/payroll").RequireAuthorization().WithTags("Payroll");

        api.MapGet("/preview", async (WorkitDbContext db, HttpContext http, int year, int month, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (month is < 1 or > 12) return Results.BadRequest("Month must be 1–12.");
                    var user = http.User.ToUserContext();
                    return Results.Ok(await BuildPreviewAsync(db, user.CompanyId, year, month, ct));
                }, logger, "building the payroll preview"))
            .WithName("PayrollPreview");

        api.MapGet("/exports", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var rows = await db.PayrollExports
                        .Where(e => e.CompanyId == user.CompanyId)
                        .OrderByDescending(e => e.SentAt)
                        .Take(50)
                        .Select(e => new PayrollExport
                        {
                            Id = e.Id, CompanyId = e.CompanyId, Year = e.Year, Month = e.Month, SentAt = e.SentAt,
                            SentByUserId = e.SentByUserId, SentByName = e.SentByName, EmployeesSent = e.EmployeesSent,
                            EmployeesRead = e.EmployeesRead, SsnsNotOnRecord = e.SsnsNotOnRecord, PaydayPayoutId = e.PaydayPayoutId,
                            // The payload is for the audit trail, not the list.
                            PayloadJson = string.Empty,
                        })
                        .ToListAsync(ct);
                    return Results.Ok(rows);
                }, logger, "loading payroll exports"))
            .WithName("PayrollExports");

        // The one call that reaches Payday; the filter loads the company's credentials.
        app.MapPost("/api/payroll/exports", async (WorkitDbContext db, HttpContext http, IPaydayPayrollApi payday, int year, int month, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (month is < 1 or > 12) return Results.BadRequest("Month must be 1–12.");
                    var user = http.User.ToUserContext();
                    var preview = await BuildPreviewAsync(db, user.CompanyId, year, month, ct);
                    var entries = ToTimesheet(preview);
                    if (entries.Count == 0)
                        return Results.BadRequest("Nothing to send: no employee with an SSN has hours in this period.");

                    var result = await payday.UploadTimesheetAsync(entries);
                    if (!result.IsSuccess || result.Value is null)
                        return Results.Content(result.ErrorMessage ?? "The timesheet could not be uploaded to Payday right now.", "text/plain", statusCode: StatusCodes.Status502BadGateway);

                    var export = new PayrollExport
                    {
                        CompanyId       = user.CompanyId,
                        Year            = year,
                        Month           = month,
                        SentByUserId    = user.UserId,
                        SentByName      = await ResolveDisplayNameAsync(db, http, user, ct),
                        EmployeesSent   = entries.Count,
                        EmployeesRead   = result.Value.TotalEmployeesRead,
                        SsnsNotOnRecord = string.Join(",", result.Value.EmployeeSSNsNotOnRecord ?? []),
                        PaydayPayoutId  = result.Value.PayoutId == Guid.Empty ? null : result.Value.PayoutId,
                        PayloadJson     = JsonSerializer.Serialize(entries),
                    };
                    db.PayrollExports.Add(export);
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(export);
                }, logger, "uploading the payroll timesheet"))
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payroll")
            .WithName("CreatePayrollExport");
    }

    /// <summary>Per-employee totals of the month's time entries, with the company's item names.</summary>
    internal static async Task<PayrollPreview> BuildPreviewAsync(WorkitDbContext db, Guid companyId, int year, int month, CancellationToken ct)
    {
        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct);

        var totals = await db.TimeEntries
            .Where(t => t.CompanyId == companyId && t.WorkDate >= from && t.WorkDate < to)
            .GroupBy(t => t.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Hours = g.Sum(t => t.Hours), Overtime = g.Sum(t => t.OvertimeHours), Driving = g.Sum(t => t.DrivingUnits), Count = g.Count() })
            .ToListAsync(ct);

        var employeeIds = totals.Select(t => t.EmployeeId).ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.CompanyId == companyId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var rows = totals
            .Select(t =>
            {
                var emp = employees.GetValueOrDefault(t.EmployeeId);
                var ssn = NormalizeSsn(emp?.Ssn);
                return new PayrollPreviewRow(t.EmployeeId, emp?.DisplayName ?? "Unknown", ssn, ssn.Length > 0, t.Hours, t.Overtime, t.Driving, t.Count);
            })
            .OrderBy(r => r.EmployeeName)
            .ToList();

        var last = await db.PayrollExports.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Year == year && e.Month == month)
            .OrderByDescending(e => e.SentAt)
            .FirstOrDefaultAsync(ct);
        if (last is not null) last.PayloadJson = string.Empty;

        return new PayrollPreview(year, month, company.PayrollRegularItemName, company.PayrollOvertimeItemName, company.PayrollDrivingItemName, rows, last);
    }

    /// <summary>
    /// Payday's timesheet shape. Employees without an SSN are skipped (Payday
    /// matches on SSN only); zero items are left out; driving only when the
    /// company named a payroll item for it.
    /// </summary>
    internal static List<TimesheetEntry> ToTimesheet(PayrollPreview preview)
    {
        var entries = new List<TimesheetEntry>();
        foreach (var row in preview.Rows.Where(r => r.HasSsn))
        {
            var items = new List<TimesheetItem>();
            if (row.RegularHours  > 0) items.Add(new TimesheetItem { Name = preview.RegularItemName,  Quantity = (double)row.RegularHours });
            if (row.OvertimeHours > 0) items.Add(new TimesheetItem { Name = preview.OvertimeItemName, Quantity = (double)row.OvertimeHours });
            if (row.DrivingUnits  > 0 && !string.IsNullOrWhiteSpace(preview.DrivingItemName))
                items.Add(new TimesheetItem { Name = preview.DrivingItemName, Quantity = row.DrivingUnits });
            if (items.Count == 0) continue;
            entries.Add(new TimesheetEntry { Ssn = row.Ssn, Name = row.EmployeeName, Items = items });
        }
        return entries;
    }

    /// <summary>Payday wants the 10 digits only; Workit stores whatever was typed (often "123456-7890").</summary>
    internal static string NormalizeSsn(string? ssn) =>
        new string((ssn ?? string.Empty).Where(char.IsDigit).ToArray());
}
