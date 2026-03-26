using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class InvoiceEndpoints
{
    internal static void MapInvoiceEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Invoices");
        var logger = app.Logger;

        securedApi.MapPost("/invoices/scan", async (
                WorkitDbContext db,
                HttpContext httpContext,
                EmailScanService emailService,
                CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    var settings = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);
                    if (settings is null)
                        return Results.BadRequest("Email settings not configured. Please configure IMAP settings first.");

                    var result = await emailService.ScanAsync(settings, userContext.CompanyId);
                    return Results.Ok(result);
                }, logger, "scanning emails for invoices"))
            .WithName("ScanInvoices");

        if (app.Environment.IsDevelopment())
        {
            securedApi.MapPost("/dev/seed-test-data", async (
                    WorkitDbContext db,
                    HttpContext httpContext,
                    Guid? companyId,
                    CancellationToken ct) =>
                    await ExecuteDbAsync(async () =>
                    {
                        // In dev, admin can seed any company. Owner seeds their own.
                        var userContext = httpContext.User.ToUserContext();
                        var targetCompanyId = companyId ?? userContext.CompanyId;

                        if (targetCompanyId == Guid.Empty)
                        {
                            // Auto-pick first company if admin has no company
                            var first = await db.Companies.FirstOrDefaultAsync(ct);
                            if (first is null) return Results.BadRequest("No companies exist. Create one first.");
                            targetCompanyId = first.Id;
                        }

                        var result = await TestDataSeeder.SeedFebruaryAsync(db, targetCompanyId);
                        return Results.Ok(result);
                    }, logger, "seeding test data"))
                .WithName("SeedTestData");

            securedApi.MapPost("/invoices/scan-folder", async (
                    WorkitDbContext db,
                    HttpContext httpContext,
                    EmailScanService emailService,
                    IConfiguration config,
                    CancellationToken ct) =>
                    await ExecuteDbAsync(async () =>
                    {
                        if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                        var userContext = httpContext.User.ToUserContext();
                        var folder = config["DevScanFolder"] ?? "TestInvoices";
                        var result = await emailService.ScanFolderAsync(folder, userContext.CompanyId);
                        return Results.Ok(result);
                    }, logger, "scanning local folder for invoices"))
                .WithName("ScanInvoicesFromFolder");
        }

        securedApi.MapGet("/invoices", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    var invoices = await db.VendorInvoices
                        .Where(x => x.CompanyId == userContext.CompanyId)
                        .OrderByDescending(x => x.InvoiceDate)
                        .ToListAsync(ct);
                    return Results.Ok(invoices);
                }, logger, "loading vendor invoices"))
            .WithName("GetInvoices");

        securedApi.MapGet("/invoices/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var userContext = httpContext.User.ToUserContext();
                    var invoice = await db.VendorInvoices.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (invoice is null) return Results.NotFound();

                    invoice.LineItems = await db.VendorInvoiceLineItems
                        .Where(x => x.InvoiceId == id)
                        .OrderBy(x => x.Description)
                        .ToListAsync(ct);

                    return Results.Ok(invoice);
                }, logger, "loading vendor invoice detail"))
            .WithName("GetInvoice");
    }
}
