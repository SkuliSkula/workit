using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class EmailSettingsEndpoints
{
    internal static void MapEmailSettingsEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Email Settings");
        var logger = app.Logger;

        securedApi.MapGet("/email-settings", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    var settings = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);
                    return settings is null ? Results.NotFound() : Results.Ok(settings);
                }, logger, "loading email settings"))
            .WithName("GetEmailSettings");

        securedApi.MapPut("/email-settings", async (WorkitDbContext db, HttpContext httpContext, EmailSettings incoming, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);

                    if (existing is null)
                    {
                        incoming.CompanyId = userContext.CompanyId;
                        db.EmailSettings.Add(incoming);
                    }
                    else
                    {
                        existing.ImapHost        = incoming.ImapHost.Trim();
                        existing.ImapPort        = incoming.ImapPort;
                        existing.UseSsl          = incoming.UseSsl;
                        existing.Username        = incoming.Username.Trim();
                        if (!string.IsNullOrWhiteSpace(incoming.Password))
                            existing.Password    = incoming.Password;
                        existing.InvoiceFolder   = incoming.InvoiceFolder.Trim();
                        existing.AutoScanEnabled = incoming.AutoScanEnabled;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing ?? incoming);
                }, logger, "saving email settings"))
            .WithName("SaveEmailSettings");

        securedApi.MapPost("/email-settings/test", async (WorkitDbContext db, HttpContext httpContext, EmailSettings testSettings, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    try
                    {
                        await EmailScanService.TestConnectionAsync(testSettings);
                        return Results.Ok(new { ok = true, message = "Connection successful." });
                    }
                    catch (Exception ex)
                    {
                        return Results.Ok(new { ok = false, message = ex.Message });
                    }
                }, logger, "testing email connection"))
            .WithName("TestEmailConnection");
    }
}
